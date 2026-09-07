namespace AudiobookServer.Core.Entities;

public class Book
{
    public Guid Id { get; set; }

    public required string Title { get; set; }
    public string? Subtitle { get; set; }
    public string? Author { get; set; }
    public string? Narrator { get; set; }
    public string? Description { get; set; }
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