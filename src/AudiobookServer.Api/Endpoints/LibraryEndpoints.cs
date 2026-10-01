using AudiobookServer.Api.Auth;
using AudiobookServer.Core.Data;
using AudiobookServer.Core.Entities;
using AudiobookServer.Core.Scanning;
using Microsoft.EntityFrameworkCore;

namespace AudiobookServer.Api.Endpoints;

/// <summary>IsPublic defaults to false: a new library is private until said otherwise.</summary>
public sealed record CreateLibraryRequest(string Name, string RootPath, bool IsPublic = false, string? Credit = null);

/// <summary>Only the fields present change. An empty Credit removes it.</summary>
public sealed record UpdateLibraryRequest(string? Name, bool? IsPublic, string? Credit);

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
                    l.IsPublic,
                    l.Credit,
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
                    l.IsPublic,
                    l.Credit,
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
                IsPublic = req.IsPublic,
                Credit = string.IsNullOrWhiteSpace(req.Credit) ? null : req.Credit.Trim(),
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.Libraries.Add(library);
            await db.SaveChangesAsync();

            return Results.Created(
                $"/api/libraries/{library.Id}",
                new { library.Id, library.Name, library.RootPath, library.IsPublic, library.Credit });
        });

        // Making a library public or private takes effect on the next request: visibility
        // is checked per request, never cached in a session.
        libraries.MapPatch("/{id:guid}", async (Guid id, UpdateLibraryRequest req, AudiobookDbContext db, CancellationToken ct) =>
        {
            var library = await db.Libraries.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (library is null)
                return Results.NotFound();

            if (req.Name is not null)
            {
                if (string.IsNullOrWhiteSpace(req.Name))
                    return Results.BadRequest(new { error = "name must not be empty." });
                library.Name = req.Name.Trim();
            }

            if (req.IsPublic is { } isPublic)
                library.IsPublic = isPublic;

            if (req.Credit is not null)
                library.Credit = string.IsNullOrWhiteSpace(req.Credit) ? null : req.Credit.Trim();

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { library.Id, library.Name, library.RootPath, library.IsPublic, library.Credit });
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
