namespace AudiobookServer.Core.Entities;

public class Library
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Absolute path on the server that this library scans.</summary>
    public required string RootPath { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastScanStartedAt { get; set; }
    public DateTimeOffset? LastScanCompletedAt { get; set; }

    public List<Book> Books { get; set; } = [];
}