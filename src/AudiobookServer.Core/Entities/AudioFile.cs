using AudiobookServer.Core.Media;

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

    /// <summary>The duration the timeline uses. See <see cref="DurationSource"/>.</summary>
    public double DurationSeconds { get; set; }

    /// <summary>
    /// ffprobe's header estimate, kept for comparison. Equals DurationSeconds when the
    /// source is Header; the difference is the correction packet counting applied.
    /// </summary>
    public double HeaderDurationSeconds { get; set; }

    public DurationSource DurationSource { get; set; }

    public long SizeBytes { get; set; }
    public string? MimeType { get; set; }

    /// <summary>The audio stream's codec (mp3, aac), not the container format.</summary>
    public string? Codec { get; set; }

    public int? Bitrate { get; set; }

    /// <summary>Set when the file was last modified on disk, so rescans can skip unchanged files.</summary>
    public DateTimeOffset FileModifiedAt { get; set; }

    public double EndOffsetSeconds => StartOffsetSeconds + DurationSeconds;
}
