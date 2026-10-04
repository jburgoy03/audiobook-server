using System.Security.Claims;
using AudiobookServer.Api.Auth;
using AudiobookServer.Core.Data;
using AudiobookServer.Core.Media;
using Microsoft.EntityFrameworkCore;

namespace AudiobookServer.Api.Endpoints;

/// <summary>
/// Books, covers and streams. Every query starts from <see cref="Visibility.VisibleBooks"/>:
/// a book in a library the user can't see is a 404, indistinguishable from no book.
/// </summary>
public static class BookEndpoints
{
    public static IEndpointRouteBuilder MapBookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/books", async (ClaimsPrincipal principal, AudiobookDbContext db, CancellationToken ct) =>
            await db.VisibleBooks(principal)
                // An admin's override wins over the scanned value (COALESCE in SQL).
                .OrderBy(b => b.AuthorOverride ?? b.Author).ThenBy(b => b.TitleOverride ?? b.Title)
                .Select(b => new BookSummaryDto(
                    b.Id,
                    b.TitleOverride ?? b.Title,
                    b.AuthorOverride ?? b.Author,
                    b.DurationSeconds,
                    b.CoverPath != null,
                    b.Files.Count,
                    b.Chapters.Count,
                    b.AddedAt,
                    b.ContentVersion))
                .ToListAsync(ct));

        app.MapGet("/api/books/{id:guid}", async (
            Guid id, ClaimsPrincipal principal, AudiobookDbContext db, CancellationToken ct) =>
        {
            var book = await db.VisibleBooks(principal)
                .Where(b => b.Id == id)
                .Select(b => new BookDetailDto(
                    b.Id,
                    b.TitleOverride ?? b.Title,
                    b.Subtitle,
                    b.AuthorOverride ?? b.Author,
                    b.Narrator,
                    b.Description,
                    b.DescriptionSource,
                    b.PublishedYear,
                    b.DurationSeconds,
                    b.CoverPath != null,
                    b.Library!.Credit,
                    b.Files
                        .OrderBy(f => f.Sequence)
                        .Select(f => new BookFileDto(
                            f.Sequence, f.StartOffsetSeconds, f.DurationSeconds, f.MimeType, f.SizeBytes))
                        .ToList(),
                    b.Chapters
                        .OrderBy(c => c.Sequence)
                        .Select(c => new ChapterDto(
                            c.Sequence, c.Title, c.StartOffsetSeconds, c.EndOffsetSeconds))
                        .ToList(),
                    b.ContentVersion))
                .FirstOrDefaultAsync(ct);

            return book is null ? Results.NotFound() : Results.Ok(book);
        })
        .Produces<BookDetailDto>()
        .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/books/{id:guid}/cover", async (
            Guid id, ClaimsPrincipal principal, AudiobookDbContext db, ICoverStore covers,
            HttpContext http, CancellationToken ct) =>
        {
            var coverPath = await db.VisibleBooks(principal)
                .Where(b => b.Id == id)
                .Select(b => b.CoverPath)
                .FirstOrDefaultAsync(ct);

            var fullPath = coverPath is null ? null : covers.Resolve(coverPath);
            if (fullPath is null || !File.Exists(fullPath))
                return Results.NotFound();

            var info = new FileInfo(fullPath);
            var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue(
                $"\"{info.LastWriteTimeUtc.Ticks:x}-{info.Length:x}\"");

            // The URL never changes but a rescan can replace the image, so browsers keep it
            // and revalidate each time: a 304 with no body when nothing changed.
            http.Response.Headers.CacheControl = "no-cache";

            var contentType = Path.GetExtension(fullPath).ToLowerInvariant() == ".png" ? "image/png" : "image/jpeg";
            return Results.File(fullPath, contentType, lastModified: info.LastWriteTimeUtc, entityTag: etag);
        });

        app.MapMethods("/api/books/{bookId:guid}/files/{sequence:int}/stream", ["GET", "HEAD"], async (
            Guid bookId, int sequence, ClaimsPrincipal principal, AudiobookDbContext db, CancellationToken ct) =>
        {
            // Files are addressed by (bookId, sequence) rather than by file ID: file rows are
            // replaced wholesale on rescan, and a stream URL is exactly the kind of durable
            // reference that must not point at a derived ID.
            var target = await db.VisibleBooks(principal)
                .Where(b => b.Id == bookId)
                .SelectMany(b => b.Files)
                .Where(f => f.Sequence == sequence)
                .Select(f => new
                {
                    f.RelativePath,
                    f.MimeType,
                    RootPath = f.Book!.Library!.RootPath
                })
                .FirstOrDefaultAsync(ct);

            if (target is null)
                return Results.NotFound();

            // Defence in depth. RelativePath is scanner-produced, but resolving and checking
            // containment costs one comparison and closes the path-traversal question entirely.
            var root = Path.GetFullPath(target.RootPath);
            var fullPath = Path.GetFullPath(Path.Combine(root, target.RelativePath));

            var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal))
                return Results.NotFound();

            var info = new FileInfo(fullPath);
            if (!info.Exists)
            {
                // The index and the disk disagree: the file was moved or deleted since the
                // last scan. 404 rather than 500 — this is a stale index, not a server fault.
                return Results.NotFound();
            }

            // Weak-ish validator built from mtime and size rather than a content hash:
            // hashing hundreds of megabytes per request is not viable, and a rescan that
            // replaces the file changes both inputs.
            var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue(
                $"\"{info.LastWriteTimeUtc.Ticks:x}-{info.Length:x}\"");

            // enableRangeProcessing hands RFC 9110 range semantics to the framework:
            // 206 with Content-Range, 416 on unsatisfiable ranges, If-Range, multi-range.
            return Results.File(
                fullPath,
                contentType: target.MimeType ?? "application/octet-stream",
                lastModified: info.LastWriteTimeUtc,
                entityTag: etag,
                enableRangeProcessing: true);
        });

        return app;
    }
}
