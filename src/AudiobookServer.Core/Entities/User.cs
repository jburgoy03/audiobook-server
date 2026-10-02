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

    /// <summary>
    /// Set when an admin hands out a temporary passphrase (new account or reset), cleared
    /// when the user chooses their own. While set, the server answers 403 to everything
    /// but "who am I", change-password and logout: a temporary passphrase travels (a text,
    /// a note), so "must change" has to be enforced, not merely suggested by the client.
    /// </summary>
    public bool MustChangePassword { get; set; }

    public List<Device> Devices { get; set; } = [];
    public List<PlaybackPosition> Positions { get; set; } = [];
}
