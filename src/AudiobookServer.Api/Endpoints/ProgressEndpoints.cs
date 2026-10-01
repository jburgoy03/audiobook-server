using System.Security.Claims;
using AudiobookServer.Api.Auth;
using AudiobookServer.Core.Data;
using AudiobookServer.Core.Entities;
using AudiobookServer.Core.Progress;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AudiobookServer.Api.Endpoints;

/// <summary>
/// What a client sends. <c>DeviceId</c> is chosen by the client (the web client keeps a
/// UUID per browser) and registered on first use. <c>Override</c>: see ProgressRules.
/// </summary>
public sealed record ProgressReport(
    Guid BookId,
    double PositionSeconds,
    DateTimeOffset ReportedAt,
    Guid? DeviceId,
    string? DeviceName,
    bool IsFinished,
    bool Override);

/// <summary>What the server holds for one book.</summary>
public sealed record ProgressDto(
    Guid BookId,
    double PositionSeconds,
    DateTimeOffset ReportedAt,
    DateTimeOffset UpdatedAt,
    Guid? DeviceId,
    string? DeviceName,
    bool IsFinished);

/// <summary>
/// The outcome of a report. Rejection is not an error: the response carries what the
/// server kept instead, so the client can tell the listener another device is ahead.
/// </summary>
public sealed record ProgressResult(bool Accepted, ProgressReason Reason, ProgressDto Progress);

public static class ProgressEndpoints
{
    // Clients report in client time. A clock running fast would make every report
    // from that device look newest, so nothing is allowed to claim the future.
    private static readonly TimeSpan AllowedClockLead = TimeSpan.FromMinutes(1);

    public static IEndpointRouteBuilder MapProgressEndpoints(this IEndpointRouteBuilder app)
    {
        // Every position for the signed-in user, most recent first. Feeds the web
        // client's progress store ("continue listening", resume points). Positions in a
        // library that has since become private stay in the table but aren't returned.
        app.MapGet("/api/progress", async (ClaimsPrincipal principal, AudiobookDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var visible = db.VisibleBooks(principal);
            return await db.PlaybackPositions
                .AsNoTracking()
                .Where(p => p.UserId == userId && visible.Any(b => b.Id == p.BookId))
                .OrderByDescending(p => p.ReportedAt)
                .Select(p => new ProgressDto(
                    p.BookId, p.PositionSeconds, p.ReportedAt, p.UpdatedAt,
                    p.DeviceId, p.Device != null ? p.Device.Name : null, p.IsFinished))
                .ToListAsync(ct);
        });

        app.MapGet("/api/books/{bookId:guid}/progress", async (
            Guid bookId, ClaimsPrincipal principal, AudiobookDbContext db, CancellationToken ct) =>
        {
            // 404 for a book the user can't see; 204 is "visible, but not started".
            if (!await db.VisibleBooks(principal).AnyAsync(b => b.Id == bookId, ct))
                return Results.NotFound();

            var userId = principal.GetUserId();
            var progress = await db.PlaybackPositions
                .AsNoTracking()
                .Where(p => p.UserId == userId && p.BookId == bookId)
                .Select(p => new ProgressDto(
                    p.BookId, p.PositionSeconds, p.ReportedAt, p.UpdatedAt,
                    p.DeviceId, p.Device != null ? p.Device.Name : null, p.IsFinished))
                .FirstOrDefaultAsync(ct);

            return progress is null ? Results.NoContent() : Results.Ok(progress);
        });

        app.MapPost("/api/progress", async (
            ProgressReport report, ClaimsPrincipal principal, AudiobookDbContext db, CancellationToken ct) =>
        {
            if (!double.IsFinite(report.PositionSeconds))
                return Results.BadRequest(new { error = "positionSeconds must be a finite number." });

            var userId = principal.GetUserId();

            // Two reports for a book with no row yet (or from a device not yet
            // registered) can both try to insert; the loser hits a unique key and
            // simply runs again, now seeing the winner's row.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await ApplyAsync(report, principal, userId, db, ct);
                }
                catch (DbUpdateException ex) when (attempt < 3 && IsUniqueViolation(ex))
                {
                    db.ChangeTracker.Clear();
                }
            }
        });

        return app;
    }

    private static async Task<IResult> ApplyAsync(
        ProgressReport report, ClaimsPrincipal principal, Guid userId, AudiobookDbContext db, CancellationToken ct)
    {
        var duration = await db.VisibleBooks(principal)
            .Where(b => b.Id == report.BookId)
            .Select(b => (double?)b.DurationSeconds)
            .FirstOrDefaultAsync(ct);

        // Most often a book removed by a rescan while a client still had it. Also a book
        // the user can't see: same answer, so a report can't probe for private books.
        if (duration is null)
            return Results.NotFound();

        var now = DateTimeOffset.UtcNow;
        var reportedAt = report.ReportedAt == default || report.ReportedAt > now + AllowedClockLead
            ? now
            : report.ReportedAt;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var deviceId = await ResolveDeviceAsync(report, userId, now, db, ct);

        // Locks the row until commit, so two devices reporting at once are decided one
        // after the other against the latest stored value, rather than both reading
        // the same old row and the last write silently winning. Not composed with LINQ
        // (no FirstOrDefault): EF would wrap it in a subquery, and the lock belongs on
        // the outer statement.
        var rows = await db.PlaybackPositions
            .FromSql($"""
                SELECT * FROM "PlaybackPositions"
                WHERE "UserId" = {userId} AND "BookId" = {report.BookId}
                FOR UPDATE
                """)
            .ToListAsync(ct);
        var stored = rows.SingleOrDefault();

        var incoming = new ProgressPoint(
            Math.Clamp(report.PositionSeconds, 0, duration.Value),
            reportedAt,
            deviceId,
            report.IsFinished);

        var decision = ProgressRules.Decide(
            stored is null ? null : new ProgressPoint(stored.PositionSeconds, stored.ReportedAt, stored.DeviceId, stored.IsFinished),
            incoming,
            report.Override);

        if (decision.Accepted)
        {
            if (stored is null)
            {
                stored = new PlaybackPosition { Id = Guid.NewGuid(), UserId = userId, BookId = report.BookId };
                db.PlaybackPositions.Add(stored);
            }

            stored.PositionSeconds = incoming.PositionSeconds;
            stored.ReportedAt = incoming.ReportedAt;
            stored.DeviceId = incoming.DeviceId;
            stored.IsFinished = incoming.IsFinished;
            stored.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // A rejected report always has a stored row: only "First" can create one.
        var deviceName = stored!.DeviceId is { } id
            ? await db.Devices.Where(d => d.Id == id).Select(d => d.Name).FirstOrDefaultAsync(ct)
            : null;

        return Results.Ok(new ProgressResult(
            decision.Accepted,
            decision.Reason,
            new ProgressDto(
                stored.BookId, stored.PositionSeconds, stored.ReportedAt, stored.UpdatedAt,
                stored.DeviceId, deviceName, stored.IsFinished)));
    }

    /// <summary>
    /// Registers the client's device on first sight and keeps its name and last-seen
    /// time current. A device ID that belongs to another user is ignored rather than
    /// trusted: the position is still saved, just without a device.
    /// </summary>
    private static async Task<Guid?> ResolveDeviceAsync(
        ProgressReport report, Guid userId, DateTimeOffset now, AudiobookDbContext db, CancellationToken ct)
    {
        if (report.DeviceId is not { } id || id == Guid.Empty)
            return null;

        var name = string.IsNullOrWhiteSpace(report.DeviceName)
            ? null
            : report.DeviceName.Trim()[..Math.Min(report.DeviceName.Trim().Length, 200)];

        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null)
        {
            db.Devices.Add(new Device
            {
                Id = id,
                UserId = userId,
                Name = name ?? "Unknown device",
                FirstSeenAt = now,
                LastSeenAt = now,
            });
            return id;
        }

        if (device.UserId != userId)
            return null;

        device.LastSeenAt = now;
        if (name is not null)
            device.Name = name;
        return id;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
