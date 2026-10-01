using System.Security.Claims;
using AudiobookServer.Core.Entities;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AudiobookServer.Api.Endpoints;

public sealed record LoginRequest(string Username, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record CurrentUser(string Username);

/// <summary>
/// Login, refresh, logout and "who am I". Modelled on Identity's MapIdentityApi, minus
/// registration, email confirmation and password reset, which a single-user server
/// doesn't want exposed.
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
            LoginRequest request, bool? useCookies, SignInManager<User> signIn) =>
        {
            signIn.AuthenticationScheme = useCookies == true
                ? IdentityConstants.ApplicationScheme
                : IdentityConstants.BearerScheme;

            var result = await signIn.PasswordSignInAsync(
                request.Username, request.Password, isPersistent: true, lockoutOnFailure: true);

            if (result.IsLockedOut)
            {
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
        // signed in: 200 or 401.
        auth.MapGet("/me", (ClaimsPrincipal user) =>
            TypedResults.Ok(new CurrentUser(user.Identity?.Name ?? "")));

        return app;
    }
}
