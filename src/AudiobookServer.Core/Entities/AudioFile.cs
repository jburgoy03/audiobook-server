namespace AudiobookServer.Core.Entities;

public class AudioFile
{
    public Guid Id { get; set; }

    public Guid BookId { get; set; }
    public Book? Book { get; set; }

    /// <summary>Path to the file, relative to the library root.</summary>
    public required string RelativePath { get; set; }

    /// <summary>Playback order within the book, zero-based.</summary>
    public int Sequence { get; set; }

    /// <summary>Where this file begins on the book's timeline.</summary>
    public double StartOffsetSeconds { get; set; }

    public double DurationSeconds { get; set; }

    public long SizeBytes { get; set; }
    public string? MimeType { get; set; }
    public string? Codec { get; set; }
    public int? Bitrate { get; set; }

    /// <summary>Set when the file was last modified on disk, so rescans can skip unchanged files.</summary>
    public DateTimeOffset FileModifiedAt { get; set; }

    public double EndOffsetSeconds => StartOffsetSeconds + DurationSeconds;
}