using AudiobookServer.Core.Data;
using Microsoft.EntityFrameworkCore;
using AudiobookServer.Core.Media;
using AudiobookServer.Core.Scanning;
using AudiobookServer.Core.Entities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AudiobookDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSingleton<IMediaProbe>(_ => new FfprobeMediaProbe());

// Extracted covers live outside the library, which is treated as read-only.
// Override with Covers:Directory (e.g. a mounted volume in a container).
var coverDirectory = builder.Configuration["Covers:Directory"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "data", "covers");
builder.Services.AddSingleton<ICoverStore>(_ => new FfmpegCoverStore(coverDirectory));
builder.Services.AddSingleton<ILibraryWalker, LibraryWalker>();
builder.Services.AddScoped<IBookScanner, BookScanner>();
builder.Services.AddScoped<ILibraryScanService, LibraryScanService>();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// No UseHttpsRedirection: TLS terminates at Cloudflare in production, and in
// development the web client reaches the API over plain HTTP through Vite's proxy.

app.MapGet("/api/libraries", async (AudiobookDbContext db, CancellationToken ct) =>
    await db.Libraries
        .AsNoTracking()
        .OrderBy(l => l.Name)
        .Select(l => new
        {
            l.Id,
            l.Name,
            l.RootPath,
            l.LastScanStartedAt,
            l.LastScanCompletedAt,
            Books = l.Books.Count
        })
        .ToListAsync(ct));

app.MapGet("/api/libraries/{id:guid}", async (Guid id, AudiobookDbContext db, CancellationToken ct) =>
{
    var library = await db.Libraries
        .AsNoTracking()
        .Where(l => l.Id == id)
        .Select(l => new
        {
            l.Id,
            l.Name,
            l.RootPath,
            l.LastScanStartedAt,
            l.LastScanCompletedAt,
            Books = l.Books.Count
        })
        .FirstOrDefaultAsync(ct);

    return library is null ? Results.NotFound() : Results.Ok(library);
});

app.MapPost("/api/libraries", async (CreateLibraryRequest req, AudiobookDbContext db) =>
{
    if (!Directory.Exists(req.RootPath))
        return Results.BadRequest(new { error = $"Path not found on server: {req.RootPath}" });

    var library = new Library
    {
        Id = Guid.NewGuid(),
        Name = req.Name,
        RootPath = req.RootPath,
        CreatedAt = DateTimeOffset.UtcNow
    };

    db.Libraries.Add(library);
    await db.SaveChangesAsync();

    return Results.Created($"/api/libraries/{library.Id}", new { library.Id, library.Name, library.RootPath });
});

app.MapPost("/api/libraries/{id:guid}/scan", async (
    Guid id, bool? force, ILibraryScanService scanner, CancellationToken ct) =>
{
    var report = await scanner.ScanAsync(id, force ?? false, ct);
    return Results.Ok(report);
});

app.MapGet("/api/books", async (AudiobookDbContext db) =>
    await db.Books
        .OrderBy(b => b.Author).ThenBy(b => b.Title)
        .Select(b => new
        {
            b.Id,
            b.Title,
            b.Author,
            b.DurationSeconds,
            HasCover = b.CoverPath != null,
            Files = b.Files.Count,
            Chapters = b.Chapters.Count
        })
        .ToListAsync());

        
app.MapGet("/api/books/{id:guid}", async (Guid id, AudiobookDbContext db, CancellationToken ct) =>
{
    var book = await db.Books
        .AsNoTracking()
        .Where(b => b.Id == id)
        .Select(b => new
        {
            b.Id,
            b.Title,
            b.Subtitle,
            b.Author,
            b.Narrator,
            b.Description,
            b.PublishedYear,
            b.DurationSeconds,
            HasCover = b.CoverPath != null,
            // Files are derived data replaced wholesale on rescan, so their IDs are
            // deliberately absent: clients address them by Sequence, which survives.
            Files = b.Files
                .OrderBy(f => f.Sequence)
                .Select(f => new
                {
                    f.Sequence,
                    f.StartOffsetSeconds,
                    f.DurationSeconds,
                    f.MimeType,
                    f.SizeBytes
                })
                .ToList(),
            Chapters = b.Chapters
                .OrderBy(c => c.Sequence)
                .Select(c => new
                {
                    c.Sequence,
                    c.Title,
                    c.StartOffsetSeconds,
                    c.EndOffsetSeconds
                })
                .ToList()
        })
        .FirstOrDefaultAsync(ct);

    return book is null ? Results.NotFound() : Results.Ok(book);
});

app.MapGet("/api/books/{id:guid}/cover", async (
    Guid id, AudiobookDbContext db, ICoverStore covers, HttpContext http, CancellationToken ct) =>
{
    var coverPath = await db.Books
        .AsNoTracking()
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
    Guid bookId, int sequence, AudiobookDbContext db, CancellationToken ct) =>
{
    // Files are addressed by (bookId, sequence) rather than by file ID: file rows are
    // replaced wholesale on rescan, and a stream URL is exactly the kind of durable
    // reference that must not point at a derived ID.
    var target = await db.AudioFiles
        .AsNoTracking()
        .Where(f => f.BookId == bookId && f.Sequence == sequence)
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

app.Run();

record CreateLibraryRequest(string Name, string RootPath);
