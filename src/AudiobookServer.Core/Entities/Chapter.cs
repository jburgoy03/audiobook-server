namespace AudiobookServer.Core.Entities;

public class Chapter
{
    public Guid Id { get; set; }

    public Guid BookId { get; set; }
    public Book? Book { get; set; }

    public required string Title { get; set; }
    public int Sequence { get; set; }

    /// <summary>Chapter bounds on the book's timeline, not within any one file.</summary>
    public double StartOffsetSeconds { get; set; }
    public double EndOffsetSeconds { get; set; }

    public double DurationSeconds => EndOffsetSeconds - StartOffsetSeconds;
}