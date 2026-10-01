namespace AudiobookServer.Core.Progress;

/// <summary>A position as stored, or as reported, reduced to what the conflict rule needs.</summary>
public sealed record ProgressPoint(
    double PositionSeconds,
    DateTimeOffset ReportedAt,
    Guid? DeviceId,
    bool IsFinished);

/// <summary>
/// Why a report was accepted or rejected. Returned to the client, so a device can
/// tell "the server is ahead of you" apart from "saved".
/// </summary>
public enum ProgressReason
{
    /// <summary>Nothing was stored for this book yet.</summary>
    First,

    /// <summary>The client said this position is deliberate: see <see cref="ProgressRules.Decide"/>.</summary>
    Override,

    /// <summary>A newer report from the device that wrote the stored one. Allows rewinding.</summary>
    SameDevice,

    /// <summary>The report marks the book finished, which is past any position.</summary>
    Finished,

    /// <summary>At or past the stored position: furthest wins.</summary>
    Further,

    /// <summary>Rejected: behind the stored position, from another device or an older report.</summary>
    Behind,

    /// <summary>Rejected: the book is stored as finished, and this report doesn't override it.</summary>
    BehindFinished,
}

public sealed record ProgressDecision(bool Accepted, ProgressReason Reason);

/// <summary>
/// The sync conflict rule. Pure: no clock, no database, so every case is unit-testable.
///
/// The baseline is <b>furthest wins</b>. Reports can arrive late and out of order (an
/// offline phone uploading hours later), and losing listening progress is worse than
/// briefly showing a position that's too far ahead. Furthest-wins alone would make
/// rewinding impossible, though, so two kinds of report bypass it:
///
/// <list type="bullet">
/// <item><b>Same device, newer report.</b> A device's own later report supersedes its
/// own earlier one, even when it's behind: that's the listener rewinding.</item>
/// <item><b>Override.</b> The client asserts the position is deliberate. The web
/// client sets it while a book is playing <i>and</i> it has already reconciled with the
/// server at start (the server wasn't ahead, or the listener declined to jump). The
/// device that's playing is authoritative.</item>
/// </list>
///
/// Known gaps, stated rather than solved:
/// <list type="bullet">
/// <item>An old report that is far ahead (a stale phone position arriving after a
/// deliberate rewind on another device) is accepted. Nothing is stored to flag it;
/// instead, the next device to start the book sees "further ahead on another device"
/// and can decline.</item>
/// <item><see cref="ProgressPoint.ReportedAt"/> is client time, so clock skew between
/// devices affects the same-device rule only (one device, one clock), and the server
/// clamps future timestamps to its own now.</item>
/// </list>
/// </summary>
public static class ProgressRules
{
    public static ProgressDecision Decide(ProgressPoint? stored, ProgressPoint incoming, bool isOverride)
    {
        if (stored is null)
            return new(true, ProgressReason.First);

        if (isOverride)
            return new(true, ProgressReason.Override);

        if (incoming.DeviceId is { } device && device == stored.DeviceId &&
            incoming.ReportedAt >= stored.ReportedAt)
            return new(true, ProgressReason.SameDevice);

        // "Finished" is its own state, ranked past every position.
        if (incoming.IsFinished)
            return new(true, ProgressReason.Finished);

        if (stored.IsFinished)
            return new(false, ProgressReason.BehindFinished);

        // Ties are accepted, so the stored row records the latest device to get there.
        return incoming.PositionSeconds >= stored.PositionSeconds
            ? new(true, ProgressReason.Further)
            : new(false, ProgressReason.Behind);
    }
}
