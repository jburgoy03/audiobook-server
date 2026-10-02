namespace AudiobookServer.Core.Entities;

public class Book
{
    public Guid Id { get; set; }

    /// <summary>As the scanner read it (tags, then folder name). Rewritten by every rescan.</summary>
    public required string Title { get; set; }
    public string? Subtitle { get; set; }

    /// <summary>As the scanner read it. Rewritten by every rescan.</summary>
    public string? Author { get; set; }
    public string? Narrator { get; set; }

    /// <summary>
    /// Set by an admin when the scanned title is wrong. The scanner never writes it, so
    /// it survives rescans; the API shows it instead of <see cref="Title"/> when set.
    /// </summary>
    public string? TitleOverride { get; set; }

    /// <summary>Same as <see cref="TitleOverride"/>, for <see cref="Author"/>.</summary>
    public string? AuthorOverride { get; set; }
    /// <summary>
    /// The blurb, fetched from a catalogue when the admin asks (<c>BlurbFetcher</c>).
    /// The scanner never writes it, so it survives rescans.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>Where <see cref="Description"/> came from: "Open Library" or "Google Books".</summary>
    public string? DescriptionSource { get; set; }
    public string? Isbn { get; set; }
    public int? PublishedYear { get; set; }

    /// <summary>Path to the book's folder, relative to the library root.</summary>
    public required string RelativePath { get; set; }

    /// <summary>Total playable length of the book, summed across all files.</summary>
    public double DurationSeconds { get; set; }

    public string? CoverPath { get; set; }

    public Guid LibraryId { get; set; }
    public Library? Library { get; set; }

    public DateTimeOffset AddedAt { get; set; }
    public DateTimeOffset? LastScannedAt { get; set; }

    public List<AudioFile> Files { get; set; } = [];
    public List<Chapter> Chapters { get; set; } = [];
}