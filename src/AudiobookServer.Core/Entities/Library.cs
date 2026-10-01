namespace AudiobookServer.Core.Entities;

public class Library
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Absolute path on the server that this library scans.</summary>
    public required string RootPath { get; set; }

    /// <summary>
    /// Whether every signed-in user can see this library's books. False (the default)
    /// means admins only. Visibility belongs to the library, and so to a folder on disk,
    /// rather than to each book: keeping public material in its own folder makes it
    /// impossible for one private book to slip into the public set.
    /// </summary>
    public bool IsPublic { get; set; }

    /// <summary>Shown with each of the library's books, e.g. "Public domain · LibriVox".</summary>
    public string? Credit { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastScanStartedAt { get; set; }
    public DateTimeOffset? LastScanCompletedAt { get; set; }

    public List<Book> Books { get; set; } = [];
}