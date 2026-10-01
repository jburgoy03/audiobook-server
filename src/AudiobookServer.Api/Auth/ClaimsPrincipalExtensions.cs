using System.Security.Claims;

namespace AudiobookServer.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The signed-in user's ID. Only call this on endpoints that require authentication,
    /// where Identity's principal always carries a NameIdentifier claim.
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("No user ID claim; is this endpoint anonymous?"));

    /// <summary>
    /// Whether this principal carries the admin claim. Use it to shape a response
    /// (what a user can see); access to admin endpoints is the Admin policy's job.
    /// </summary>
    public static bool IsAdmin(this ClaimsPrincipal principal) =>
        principal.HasClaim(AuthPolicies.AdminClaim, "true");
}
