using AudiobookServer.Core.Data;
using Microsoft.EntityFrameworkCore;
using AudiobookServer.Core.Media;
using AudiobookServer.Core.Scanning;
using AudiobookServer.Core.Entities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AudiobookDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSingleton<IMediaProbe>(_ => new FfprobeMediaProbe());
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

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

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

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
