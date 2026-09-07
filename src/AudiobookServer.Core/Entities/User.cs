namespace AudiobookServer.Core.Entities;

public class User
{
    public Guid Id { get; set; }

    public required string Username { get; set; }
    public required string PasswordHash { get; set; }

    public bool IsAdmin { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<Device> Devices { get; set; } = [];
    public List<PlaybackPosition> Positions { get; set; } = [];
}