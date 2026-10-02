using AudiobookServer.Core.Data;
using AudiobookServer.Core.Entities;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace AudiobookServer.Api.Auth;

/// <summary>
/// One user, two kinds of client.
///
/// The web client authenticates with a cookie. Its media elements (&lt;audio src&gt;,
/// &lt;img src&gt;) can't attach an Authorization header, but they do send cookies, and
/// the client is always same-origin (Vite's proxy in development, served by this API
/// in production), so a SameSite=Strict, HttpOnly cookie covers every request it makes.
///
/// The Android client sends bearer tokens. Media3 can attach headers to its requests,
/// so it doesn't need cookies.
///
/// A policy scheme picks between them per request: an "Authorization: Bearer" header
/// means the bearer scheme, anything else means the cookie.
///
/// Tokens are Identity's own (AddBearerToken), not JWTs: opaque, protected with the
/// same data-protection keys as the cookie, and refreshable with a check of the user's
/// security stamp (so changing the password revokes them). That trades away
/// third-party verifiability, which nothing here needs, for not hand-rolling refresh
/// token storage and rotation.
/// </summary>
public static class AuthSetup
{
    public const string CookieOrBearer = "CookieOrBearer";

    public static IServiceCollection AddAudiobookAuth(
        this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        // Cookies and tokens are both encrypted with these keys. Kept in memory they would
        // change on every restart and sign everyone out, so they're persisted: under
        // data/keys locally, and on a volume in a container (DataProtection:KeysDirectory).
        var keysDirectory = config["DataProtection:KeysDirectory"]
            ?? Path.Combine(env.ContentRootPath, "data", "keys");
        services.AddDataProtection()
            .SetApplicationName("AudiobookServer")
            .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));

        services
            .AddAuthentication(o =>
            {
                o.DefaultScheme = CookieOrBearer;
                o.DefaultChallengeScheme = CookieOrBearer;
            })
            .AddPolicyScheme(CookieOrBearer, "Cookie or bearer token", o =>
            {
                o.ForwardDefaultSelector = ctx =>
                    ctx.Request.Headers.Authorization.ToString()
                        .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                        ? IdentityConstants.BearerScheme
                        : IdentityConstants.ApplicationScheme;
            })
            .AddBearerToken(IdentityConstants.BearerScheme, o =>
            {
                o.BearerTokenExpiration = TimeSpan.FromHours(1);
                o.RefreshTokenExpiration = TimeSpan.FromDays(30);
            })
            .AddIdentityCookies();

        // After AddIdentityCookies, which sets up the cookie's Events (including the
        // security-stamp check); this only changes properties on them.
        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "audiobook.auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            // Secure whenever the request was HTTPS. Behind Cloudflare that's decided by
            // X-Forwarded-Proto (see UseForwardedHeaders). "Always" would break plain
            // HTTP over Tailscale, where browsers refuse to store a Secure cookie.
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromDays(30);
            o.SlidingExpiration = true;

            // An API answers 401/403. The defaults redirect to /Account/Login, which
            // would turn every unauthenticated fetch into a 302 to a page that
            // doesn't exist.
            o.Events.OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            o.Events.OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services
            .AddIdentityCore<User>(o =>
            {
                // One person, choosing a passphrase: length is what matters, and
                // character-class rules mostly produce "Password1!".
                o.Password.RequiredLength = 12;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;

                // The login endpoint will face the internet eventually.
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddEntityFrameworkStores<AudiobookDbContext>()
            .AddClaimsPrincipalFactory<AudiobookClaimsPrincipalFactory>()
            .AddSignInManager();

        // A cookie is checked against the user's security stamp at most this often. Every
        // account change that must end sessions (passphrase changed or reset, disabled,
        // admin granted or removed) updates the stamp, so this is how long a browser can
        // keep going after one. The default is 30 minutes; 5 costs one small query per
        // signed-in user per 5 minutes. Refresh tokens are checked on every refresh;
        // access tokens live out their hour regardless (the accepted gap).
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

        // Secure by default: every endpoint needs a signed-in user whose passphrase is
        // settled, unless it says otherwise. Login, refresh, logout and the web client's
        // shell opt out entirely; "who am I" and change-password ask only for SignedIn,
        // so a user holding a temporary passphrase can do exactly those and nothing else
        // (403 everywhere else).
        // Admin endpoints ask for more: the admin claim too. Signed out is still a 401
        // (challenge); signed in without the claim is a 403.
        // Named policies replace the fallback rather than adding to it, which is why the
        // settled-passphrase rule is repeated in Admin.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(CookieOrBearer)
                .RequireAuthenticatedUser()
                .AddRequirements(new PassphraseSettledRequirement())
                .Build())
            .AddPolicy(AuthPolicies.SignedIn, p => p
                .AddAuthenticationSchemes(CookieOrBearer)
                .RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.Admin, p => p
                .AddAuthenticationSchemes(CookieOrBearer)
                .RequireAuthenticatedUser()
                .RequireClaim(AuthPolicies.AdminClaim, "true")
                .AddRequirements(new PassphraseSettledRequirement()));

        services.AddScoped<AccountAdmin>();

        return services;
    }
}

/// <summary>The user doesn't hold a temporary passphrase (no must-change claim).</summary>
public sealed class PassphraseSettledRequirement
    : AuthorizationHandler<PassphraseSettledRequirement>, IAuthorizationRequirement
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PassphraseSettledRequirement requirement)
    {
        if (!context.User.MustChangePassword())
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
