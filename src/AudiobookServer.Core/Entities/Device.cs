namespace AudiobookServer.Core.Entities;

public class Device
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Human-readable name the client reports, e.g. "Pixel 8" or "Firefox on desktop".</summary>
    public required string Name { get; set; }

    /// <summary>Client platform, used to explain conflicts back to the user.</summary>
    public string? Platform { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}