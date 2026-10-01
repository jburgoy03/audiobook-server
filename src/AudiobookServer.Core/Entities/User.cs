using Microsoft.AspNetCore.Identity;

namespace AudiobookServer.Core.Entities;

/// <summary>
/// An account. Identity owns the credentials (UserName, PasswordHash, SecurityStamp,
/// lockout counters); this class adds what the app itself needs.
/// </summary>
public class User : IdentityUser<Guid>
{
    public bool IsAdmin { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<Device> Devices { get; set; } = [];
    public List<PlaybackPosition> Positions { get; set; } = [];
}
