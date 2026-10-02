namespace AudiobookServer.Core.Entities;

/// <summary>
/// Lets one account see one library that isn't public. Admins see every library
/// without grants; public libraries need none. Decided 2026-10-02, replacing "the
/// private library is never shared": the admin chooses, account by account.
///
/// Read per request (see Visibility), not baked into a claim, so a grant or a revoke
/// applies to the very next request without ending anyone's session.
/// </summary>
public class LibraryGrant
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid LibraryId { get; set; }
    public Library? Library { get; set; }

    public DateTimeOffset GrantedAt { get; set; }
}
