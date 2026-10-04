using AudiobookServer.Api.Auth;
using AudiobookServer.Core.Data;
using AudiobookServer.Core.Entities;
using AudiobookServer.Core.Metadata;
using Microsoft.EntityFrameworkCore;

namespace AudiobookServer.Api.Endpoints;

/// <summary>
/// The book's title and author as the admin wants them shown. Both fields are always
/// sent: the editor sends what its fields hold, and an empty field clears that override.
/// </summary>
public sealed record SetMetadataRequest(string? Title, string? Author);

/// <summary>One row of GET /api/admin/books: what's shown, and what the scanner read.</summary>
public sealed record AdminBookResponse(
    Guid Id,
    string LibraryName,
    string RelativePath,
    string Title,
    string? Author,
    string ScannedTitle,
    string? ScannedAuthor,
    string? TitleOverride,
    string? AuthorOverride,
    string? Description,
    string? DescriptionSource);

/// <summary>
/// The answer to "fetch a blurb": whether one was found, the record it came from (so
/// the admin can see a wrong match), and the book as it now stands. Not found leaves
/// any existing blurb in place.
/// </summary>
public sealed record FetchBlurbResponse(
    bool Found,
    string? MatchedTitle,
    string? MatchedAuthor,
    AdminBookResponse Book);

/// <summary>
/// Correcting books' metadata from the admin page: admin only. Overrides live in their
/// own columns, which the scanner never writes, so a rescan keeps refreshing the scanned
/// values underneath without touching the admin's. Blurbs are the same: fetched on the
/// admin's request, never by a scan. Every book, every library: admins see them all, so
/// this starts from <c>db.Books</c>, not <c>VisibleBooks</c>.
/// </summary>
public static class AdminBookEndpoints
{
    public static IEndpointRouteBuilder MapAdminBookEndpoints(this IEndpointRouteBuilder app)
    {
        var books = app.MapGroup("/api/admin/books")
            .RequireAuthorization(AuthPolicies.Admin);

        books.MapGet("", async (AudiobookDbContext db, CancellationToken ct) =>
            // Ordered before the projection: EF can't translate ordering by a member of
            // a record built through its constructor.
            await Rows(db.Books
                    .OrderBy(b => b.TitleOverride ?? b.Title)
                    .ThenBy(b => b.Library!.Name))
                .ToListAsync(ct));

        books.MapPut("/{id:guid}/metadata", async (
            Guid id, SetMetadataRequest request, AudiobookDbContext db, CancellationToken ct) =>
        {
            var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (book is null)
                return Results.NotFound();

            var title = MetadataOverride.Normalize(request.Title, book.Title, MetadataOverride.MaxTitleLength);
            if (!title.Ok)
                return Results.Problem($"A title can be at most {MetadataOverride.MaxTitleLength} characters.", statusCode: StatusCodes.Status400BadRequest);

            var author = MetadataOverride.Normalize(request.Author, book.Author, MetadataOverride.MaxAuthorLength);
            if (!author.Ok)
                return Results.Problem($"An author can be at most {MetadataOverride.MaxAuthorLength} characters.", statusCode: StatusCodes.Status400BadRequest);

            book.TitleOverride = title.Value;
            book.AuthorOverride = author.Value;
            await db.SaveChangesAsync(ct);

            return Results.Ok(await RowAsync(db, id, ct));
        });

        // One book per request, so the page drives a bulk fetch book by book and no
        // request comes near Cloudflare's 100-second limit. Searches by the title and
        // author listeners see, so a corrected title searches correctly.
        books.MapPost("/{id:guid}/description/fetch", async (
            Guid id, AudiobookDbContext db, IBlurbFetcher fetcher, CancellationToken ct) =>
        {
            var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (book is null)
                return Results.NotFound();

            Blurb? blurb;
            try
            {
                blurb = await fetcher.FetchAsync(book.TitleOverride ?? book.Title, book.AuthorOverride ?? book.Author, ct);
            }
            catch (HttpRequestException ex)
            {
                return Results.Problem(
                    $"Couldn't ask the catalogues ({ex.Message}).",
                    statusCode: StatusCodes.Status502BadGateway);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return Results.Problem(
                    "The catalogues took too long to answer.",
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }

            if (blurb is not null)
            {
                book.Description = blurb.Text;
                book.DescriptionSource = blurb.Source;
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new FetchBlurbResponse(
                blurb is not null, blurb?.MatchedTitle, blurb?.MatchedAuthor, await RowAsync(db, id, ct)));
        });

        books.MapDelete("/{id:guid}/description", async (Guid id, AudiobookDbContext db, CancellationToken ct) =>
        {
            var changed = await db.Books
                .Where(b => b.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Description, (string?)null)
                    .SetProperty(b => b.DescriptionSource, (string?)null), ct);

            return changed == 0 ? Results.NotFound() : Results.Ok(await RowAsync(db, id, ct));
        });

        return app;
    }

    private static Task<AdminBookResponse> RowAsync(AudiobookDbContext db, Guid id, CancellationToken ct) =>
        Rows(db.Books.AsNoTracking().Where(b => b.Id == id)).SingleAsync(ct);

    private static IQueryable<AdminBookResponse> Rows(IQueryable<Book> books) =>
        books.Select(b => new AdminBookResponse(
            b.Id,
            b.Library!.Name,
            b.RelativePath,
            b.TitleOverride ?? b.Title,
            b.AuthorOverride ?? b.Author,
            b.Title,
            b.Author,
            b.TitleOverride,
            b.AuthorOverride,
            b.Description,
            b.DescriptionSource));
}
