namespace AudiobookServer.Api.Endpoints;

// The book contract the clients are written against. These were anonymous types
// until the mobile work: the JSON is unchanged, but named records give the OpenAPI
// document reusable schemas and make a renamed field a compile-visible change.
// Rule: additive only. A field may be added; renaming or removing one is a breaking
// change for an installed app (see docs/next-steps/mobile-backend.md).

/// <summary>One row of <c>GET /api/books</c>.</summary>
public sealed record BookSummaryDto(
    Guid Id,
    string Title,
    string? Author,
    double DurationSeconds,
    bool HasCover,
    int Files,
    int Chapters,
    DateTimeOffset AddedAt,
    // Compare with a download's stored value; different means re-download. Null for a
    // book not rescanned since content versions were added: treat as unknown.
    string? ContentVersion);

/// <summary><c>GET /api/books/{id}</c>.</summary>
public sealed record BookDetailDto(
    Guid Id,
    string Title,
    string? Subtitle,
    string? Author,
    string? Narrator,
    string? Description,
    string? DescriptionSource,
    int? PublishedYear,
    double DurationSeconds,
    bool HasCover,
    // The library's credit line, e.g. "Public domain · LibriVox".
    string? Credit,
    IReadOnlyList<BookFileDto> Files,
    IReadOnlyList<ChapterDto> Chapters,
    string? ContentVersion);

/// <summary>
/// One audio file on the book's timeline. Files are derived data replaced wholesale on
/// rescan, so their IDs are deliberately absent: clients address them by
/// <see cref="Sequence"/>, which survives.
/// </summary>
public sealed record BookFileDto(
    int Sequence,
    double StartOffsetSeconds,
    double DurationSeconds,
    string? MimeType,
    long SizeBytes);

public sealed record ChapterDto(
    int Sequence,
    string Title,
    double StartOffsetSeconds,
    double EndOffsetSeconds);
