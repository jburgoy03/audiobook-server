namespace AudiobookServer.Core.Entities;

public class PlaybackPosition
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid BookId { get; set; }
    public Book? Book { get; set; }

    /// <summary>Position on the book's timeline. One number, not a file plus an offset.</summary>
    public double PositionSeconds { get; set; }

    /// <summary>The device that reported this position.</summary>
    public Guid? DeviceId { get; set; }
    public Device? Device { get; set; }

    /// <summary>When the client says playback reached this point — not when the server stored it.</summary>
    public DateTimeOffset ReportedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsFinished { get; set; }
}