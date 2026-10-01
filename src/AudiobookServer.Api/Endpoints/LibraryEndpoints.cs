using AudiobookServer.Api.Auth;
using AudiobookServer.Core.Data;
using AudiobookServer.Core.Entities;
using AudiobookServer.Core.Scanning;
using Microsoft.EntityFrameworkCore;

namespace AudiobookServer.Api.Endpoints;

public sealed record CreateLibraryRequest(string Name, string RootPath);

/// <summary>
/// Managing libraries: admin only. These responses include server paths, and a scan
/// is minutes of disk and ffprobe work, so a listener has no business with either.
/// Listeners reach books through the book endpoints, which apply visibility.
/// </summary>
public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var libraries = app.MapGroup("/api/libraries")
            .RequireAuthorization(AuthPolicies.Admin);

        libraries.MapGet("", async (AudiobookDbContext db, CancellationToken ct) =>
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

        libraries.MapGet("/{id:guid}", async (Guid id, AudiobookDbContext db, CancellationToken ct) =>
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

        libraries.MapPost("", async (CreateLibraryRequest req, AudiobookDbContext db) =>
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

        // Runs inside the request, on the request's cancellation token: through
        // Cloudflare a scan longer than 100 seconds is cut off and cancelled. Scan over
        // Tailscale until background scanning exists.
        libraries.MapPost("/{id:guid}/scan", async (
            Guid id, bool? force, AudiobookDbContext db, ILibraryScanService scanner, CancellationToken ct) =>
        {
            if (!await db.Libraries.AnyAsync(l => l.Id == id, ct))
                return Results.NotFound();

            var report = await scanner.ScanAsync(id, force ?? false, ct);
            return Results.Ok(report);
        });

        return app;
    }
}
