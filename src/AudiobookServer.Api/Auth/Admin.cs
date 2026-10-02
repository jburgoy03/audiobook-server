using System.Security.Claims;
using AudiobookServer.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AudiobookServer.Api.Auth;

public static class AuthPolicies
{
    /// <summary>Policy for everything that manages the server: libraries, scans, users.</summary>
    public const string Admin = "Admin";

    /// <summary>
    /// Signed in, nothing more. Only for the few endpoints a user still needs while they
    /// must change their passphrase: "who am I" and change-password. Everything else
    /// goes through the fallback policy, which also requires the passphrase to be settled.
    /// </summary>
    public const string SignedIn = "SignedIn";

    /// <summary>Present, with value "true", only on an admin's principal.</summary>
    public const string AdminClaim = "admin";

    /// <summary>Present, with value "true", while the user still holds a temporary passphrase.</summary>
    public const string MustChangePasswordClaim = "must_change_password";
}

/// <summary>
/// Adds the admin claim when <see cref="User.IsAdmin"/> is set, and the
/// must-change-password claim when <see cref="User.MustChangePassword"/> is.
///
/// Every principal goes through this factory: cookie sign-in, bearer sign-in, token
/// refresh, and the cookie's periodic security-stamp revalidation. So one class
/// covers both clients, and nothing else needs to know where the claim comes from.
///
/// A claim rather than Identity roles: one flag is all this needs, and roles mean
/// moving to IdentityDbContext with two more tables and a join. Revisit if a third
/// kind of user appears.
/// </summary>
public sealed class AudiobookClaimsPrincipalFactory(UserManager<User> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<User>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(User user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.IsAdmin)
            identity.AddClaim(new Claim(AuthPolicies.AdminClaim, "true"));
        if (user.MustChangePassword)
            identity.AddClaim(new Claim(AuthPolicies.MustChangePasswordClaim, "true"));
        return identity;
    }
}

public static class AdminUserManagerExtensions
{
    /// <summary>
    /// The only supported way to change <see cref="User.IsAdmin"/>.
    ///
    /// The claim is baked into cookies and tokens when they're issued, so changing the
    /// column alone leaves every existing session with its old answer. Updating the
    /// security stamp (which also saves the user, IsAdmin included) makes those
    /// sessions stale:
    /// - the cookie is rebuilt or rejected at its next stamp validation (30 minutes),
    /// - refresh tokens are rejected immediately,
    /// - an access token already issued stays valid until it expires (1 hour). Bearer
    ///   tokens carry no stamp check of their own; that's the accepted gap.
    /// </summary>
    public static async Task<IdentityResult> SetAdminAsync(this UserManager<User> users, User user, bool isAdmin)
    {
        if (user.IsAdmin == isAdmin)
            return IdentityResult.Success;

        user.IsAdmin = isAdmin;
        return await users.UpdateSecurityStampAsync(user);
    }
}
