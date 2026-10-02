using System.Security.Cryptography;
using AudiobookServer.Core.Data;
using AudiobookServer.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AudiobookServer.Api.Auth;

public sealed record AccountSummary(
    Guid Id,
    string Username,
    bool IsAdmin,
    DateTimeOffset CreatedAt,
    bool Disabled,
    bool MustChangePassword,
    DateTimeOffset? LastSeenAt);

public enum AccountFailure { None, NotFound, Conflict, Invalid }

/// <summary>
/// The outcome of an account change. <see cref="TemporaryPassword"/> is set only when
/// the server generated one; it is never stored, so this is the only time it exists.
/// </summary>
public sealed record AccountResult(AccountFailure Failure, string? Message = null, User? User = null, string? TemporaryPassword = null)
{
    public bool Succeeded => Failure == AccountFailure.None;

    public static AccountResult Ok(User user, string? temporaryPassword = null) => new(AccountFailure.None, null, user, temporaryPassword);
    public static AccountResult Invalid(string message) => new(AccountFailure.Invalid, message);
}

/// <summary>
/// Every account change an admin can make, in one place, so the admin page
/// (/api/admin/users) and the server's `admin` command apply the same rules:
/// what ends sessions, what sets "must change", what counts as disabled.
///
/// Sessions end through the security stamp. Every change here that should end them
/// (passphrase reset or changed, disable, admin removed) updates it: refresh tokens die
/// at once, cookies at their next validation (5 minutes), access tokens within the hour.
/// </summary>
public sealed class AccountAdmin(UserManager<User> users, AudiobookDbContext db)
{
    /// <summary>
    /// A disabled account is one locked out until the end of time, which Identity's
    /// sign-in already refuses, so no other code needs to know about disabling.
    /// Not DateTimeOffset.MaxValue: Postgres stores microseconds, so MaxValue doesn't
    /// survive a round trip and an equality check on it would fail. Anything at or past
    /// this date counts; an ordinary lockout lasts five minutes.
    /// </summary>
    public static readonly DateTimeOffset DisabledUntil = new(9999, 12, 31, 0, 0, 0, TimeSpan.Zero);

    public static bool IsDisabled(User user) => user.LockoutEnd >= DisabledUntil;

    /// <summary>
    /// LastSeenAt is the latest playback report from any of their devices: free to
    /// compute, but blind to someone who signs in and never plays anything.
    /// </summary>
    public Task<List<AccountSummary>> ListAsync(CancellationToken ct = default) =>
        db.Users
            .AsNoTracking()
            .OrderBy(u => u.UserName)
            .Select(u => new AccountSummary(
                u.Id,
                u.UserName!,
                u.IsAdmin,
                u.CreatedAt,
                u.LockoutEnd >= DisabledUntil,
                u.MustChangePassword,
                u.Devices.Max(d => (DateTimeOffset?)d.LastSeenAt)))
            .ToListAsync(ct);

    /// <summary>By username, or by ID when the argument is a GUID.</summary>
    public async Task<User?> FindAsync(string usernameOrId) =>
        Guid.TryParse(usernameOrId, out var id)
            ? await users.FindByIdAsync(id.ToString())
            : await users.FindByNameAsync(usernameOrId.Trim());

    /// <summary>
    /// With no password, the server generates a temporary one and the user must change
    /// it at first sign-in. A new account can be made admin directly: it has no sessions
    /// yet whose claims could be stale.
    /// </summary>
    public async Task<AccountResult> CreateAsync(string username, string? password = null, bool isAdmin = false)
    {
        var temporary = password is null;
        password ??= TemporaryPassphrase.Generate();

        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = username?.Trim(),
            IsAdmin = isAdmin,
            MustChangePassword = temporary,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var result = await users.CreateAsync(user, password);
        if (result.Succeeded)
            return AccountResult.Ok(user, temporary ? password : null);

        // An admin may learn that a name is taken; nobody else can call this.
        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName)))
            return new AccountResult(AccountFailure.Conflict, $"The name '{user.UserName}' is already taken.");

        return AccountResult.Invalid(Describe(result));
    }

    /// <summary>
    /// Replaces the passphrase. With none given, generates a temporary one and sets
    /// MustChangePassword; with one given (the `admin` command's prompt), clears it.
    /// Clears an ordinary lockout but not a disable: that's what Enable is for.
    ///
    /// Remove-then-add rather than Identity's reset-token flow: AddIdentityCore registers
    /// no token providers, and both calls update the security stamp anyway.
    /// </summary>
    public async Task<AccountResult> ResetPasswordAsync(User user, string? password = null)
    {
        var temporary = password is null;
        password ??= TemporaryPassphrase.Generate();

        // Validate before removing the old one, so a rejected passphrase can't leave the
        // account with none at all.
        foreach (var validator in users.PasswordValidators)
        {
            var valid = await validator.ValidateAsync(users, user, password);
            if (!valid.Succeeded)
                return AccountResult.Invalid(Describe(valid));
        }

        user.MustChangePassword = temporary;

        var removed = await users.RemovePasswordAsync(user);
        if (!removed.Succeeded)
            return AccountResult.Invalid(Describe(removed));

        var added = await users.AddPasswordAsync(user, password);
        if (!added.Succeeded)
            return AccountResult.Invalid(Describe(added));

        if (!IsDisabled(user))
            await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);

        return AccountResult.Ok(user, temporary ? password : null);
    }

    public async Task<AccountResult> DisableAsync(User user, Guid? actingUserId)
    {
        if (user.Id == actingUserId)
            return AccountResult.Invalid("You can't disable your own account.");

        // Lockout must be enabled on the account for an end date to be set. Every account
        // made here has it (AllowedForNewUsers), but don't rely on that.
        await users.SetLockoutEnabledAsync(user, true);
        var locked = await users.SetLockoutEndDateAsync(user, DisabledUntil);
        if (!locked.Succeeded)
            return AccountResult.Invalid(Describe(locked));

        // Lockout only stops new sign-ins. The stamp ends the sessions already open.
        await users.UpdateSecurityStampAsync(user);
        return AccountResult.Ok(user);
    }

    /// <summary>Also the way back from a disabled admin account (the `admin enable` command).</summary>
    public async Task<AccountResult> EnableAsync(User user)
    {
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return AccountResult.Ok(user);
    }

    public async Task<AccountResult> SetAdminAsync(User user, bool isAdmin)
    {
        if (!isAdmin && user.IsAdmin && await db.Users.CountAsync(u => u.IsAdmin) <= 1)
            return AccountResult.Invalid($"{user.UserName} is the only admin. Make someone else admin first.");

        var result = await users.SetAdminAsync(user, isAdmin);
        return result.Succeeded ? AccountResult.Ok(user) : AccountResult.Invalid(Describe(result));
    }

    /// <summary>
    /// The user's own change. ChangePasswordAsync updates the stamp, so every other
    /// session of theirs ends; the caller re-issues the current one.
    /// </summary>
    public async Task<AccountResult> ChangeOwnPasswordAsync(User user, string currentPassword, string newPassword)
    {
        if (newPassword == currentPassword)
            return AccountResult.Invalid("Choose a passphrase different from the current one.");

        var changed = await users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!changed.Succeeded)
            return AccountResult.Invalid(Describe(changed));

        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            await users.UpdateAsync(user);
        }

        return AccountResult.Ok(user);
    }

    /// <summary>Identity's messages, reworded where they say "password" about a passphrase rule.</summary>
    private string Describe(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(e => e.Code switch
        {
            nameof(IdentityErrorDescriber.PasswordTooShort) =>
                $"Passphrases need at least {users.Options.Password.RequiredLength} characters.",
            nameof(IdentityErrorDescriber.PasswordMismatch) => "The current passphrase is incorrect.",
            _ => e.Description,
        }));
}

/// <summary>
/// A passphrase an admin hands over by text or reads aloud: 20 characters in groups of
/// four, like "k7mq-x2vd-9hfa-tn3r-wpe4".
///
/// Lowercase letters and digits, without the ones that get confused (0/o, 1/l; no
/// capitals, so no I either): 32 symbols, 5 bits each, 100 bits in all. Far past
/// guessable, and easy to type on a phone, which mixed case is not. 32 divides 256, so
/// GetItems' choice is unbiased.
/// </summary>
public static class TemporaryPassphrase
{
    private const string Alphabet = "abcdefghijkmnpqrstuvwxyz23456789";

    public static string Generate() =>
        string.Join('-', RandomNumberGenerator.GetItems<char>(Alphabet, 20).Chunk(4).Select(c => new string(c)));
}
