using System.Security.Claims;
using AudiobookServer.Api.Auth;
using AudiobookServer.Core.Entities;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AudiobookServer.Api.Endpoints;

public sealed record LoginRequest(string Username, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record CurrentUser(string Username, bool IsAdmin, bool MustChangePassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
/// Login, refresh, logout, "who am I" and changing your own passphrase. Modelled on
/// Identity's MapIdentityApi, minus registration, email confirmation and self-service
/// reset: accounts are made, and passphrases reset, by the admin (AdminUserEndpoints,
/// or the `admin` command on the server).
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        // ?useCookies=true for the web client: the response sets the auth cookie and has
        // no body. Without it (Android), the bearer handler writes the token response:
        // { tokenType, accessToken, expiresIn, refreshToken }.
        auth.MapPost("/login", async Task<Results<EmptyHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> (
            LoginRequest request, bool? useCookies, SignInManager<User> signIn, UserManager<User> users) =>
        {
            signIn.AuthenticationScheme = useCookies == true
                ? IdentityConstants.ApplicationScheme
                : IdentityConstants.BearerScheme;

            var result = await signIn.PasswordSignInAsync(
                request.Username, request.Password, isPersistent: true, lockoutOnFailure: true);

            if (result.IsLockedOut)
            {
                // Disabling is a lockout without end, so it lands here too, where "too many
                // attempts" would only confuse. Say "disabled", but only to someone holding
                // the right passphrase: a guesser learns nothing they didn't already know.
                // (A locked-out sign-in never checks the passphrase, so check it here; this
                // doesn't count as a failed attempt.)
                if (await users.FindByNameAsync(request.Username) is { } user &&
                    AccountAdmin.IsDisabled(user) &&
                    await users.CheckPasswordAsync(user, request.Password))
                {
                    return TypedResults.Problem(
                        "This account is disabled.", statusCode: StatusCodes.Status403Forbidden);
                }

                return TypedResults.Problem(
                    "Too many failed attempts. Try again in a few minutes.",
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            // Whatever the reason (unknown user, wrong password), the same answer.
            if (!result.Succeeded)
                return TypedResults.Unauthorized();

            // The sign-in handler has already written the cookie or the token body.
            return TypedResults.Empty;
        }).AllowAnonymous();

        // Anonymous because the access token may already have expired; the refresh token
        // is the credential. The security-stamp check means a password change ends
        // every session.
        auth.MapPost("/refresh", async Task<Results<SignInHttpResult, UnauthorizedHttpResult>> (
            RefreshRequest request, SignInManager<User> signIn, IOptionsMonitor<BearerTokenOptions> bearerOptions) =>
        {
            var protector = bearerOptions.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
            var ticket = protector.Unprotect(request.RefreshToken);

            if (ticket?.Properties.ExpiresUtc is not { } expires ||
                DateTimeOffset.UtcNow >= expires ||
                await signIn.ValidateSecurityStampAsync(ticket.Principal) is not { } user)
            {
                return TypedResults.Unauthorized();
            }

            var principal = await signIn.CreateUserPrincipalAsync(user);
            return TypedResults.SignIn(principal, authenticationScheme: IdentityConstants.BearerScheme);
        }).AllowAnonymous();

        // Clears the cookie. Anonymous so that signing out with an already-expired
        // session still succeeds. Bearer clients sign out by discarding their tokens.
        auth.MapPost("/logout", async (SignInManager<User> signIn) =>
        {
            await signIn.SignOutAsync();
            return TypedResults.NoContent();
        }).AllowAnonymous();

        // The cookie is HttpOnly, so this is how the web client learns whether it's
        // signed in: 200 or 401. IsAdmin only decides which controls the client shows,
        // and MustChangePassword which page; the server enforces both regardless.
        // SignedIn rather than the fallback, so it still answers while the passphrase is
        // temporary.
        auth.MapGet("/me", (ClaimsPrincipal user) =>
            TypedResults.Ok(new CurrentUser(user.Identity?.Name ?? "", user.IsAdmin(), user.MustChangePassword())))
            .RequireAuthorization(AuthPolicies.SignedIn);

        // Ends every other session (the stamp changes) and clears MustChangePassword.
        // A cookie session is re-issued so the browser that made the change stays signed
        // in, now without the must-change claim. A bearer client signs in again with the
        // new passphrase: its token still carries the old claims.
        auth.MapPost("/change-password", async (
            ChangePasswordRequest request, ClaimsPrincipal principal, HttpContext http,
            UserManager<User> users, SignInManager<User> signIn, AccountAdmin accounts) =>
        {
            if (await users.GetUserAsync(principal) is not { } user)
                return Results.Unauthorized();

            var result = await accounts.ChangeOwnPasswordAsync(
                user, request.CurrentPassword ?? "", request.NewPassword ?? "");
            if (!result.Succeeded)
                return Results.BadRequest(new { error = result.Message });

            var bearer = http.Request.Headers.Authorization.ToString()
                .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
            if (!bearer)
                await signIn.RefreshSignInAsync(user);

            return Results.NoContent();
        }).RequireAuthorization(AuthPolicies.SignedIn);

        return app;
    }
}
