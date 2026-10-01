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
}
