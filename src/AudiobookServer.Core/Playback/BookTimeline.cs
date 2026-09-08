using AudiobookServer.Core.Entities;

namespace AudiobookServer.Core.Playback;

/// <summary>A point on a book's timeline, resolved to a file and an offset within it.</summary>
public sealed record TimelineLocation(AudioFile File, double OffsetInFileSeconds);

/// <summary>
/// Translates book-level timeline offsets into a file plus an offset within that file.
/// Pure arithmetic: no I/O, no database access.
/// </summary>
public static class BookTimeline
{
    /// <summary>
    /// Resolves a book-level offset to the file containing it.
    /// Returns null when the offset falls outside the book's timeline.
    /// </summary>
    /// <param name="filesInSequence">
    /// The book's files, ordered by <see cref="AudioFile.Sequence"/>. Callers must order the
    /// list; this method does not sort, and will produce wrong answers on unordered input.
    /// </param>
    /// <param name="bookOffsetSeconds">Offset on the book timeline, in seconds.</param>
    public static TimelineLocation? Resolve(
        IReadOnlyList<AudioFile> filesInSequence,
        double bookOffsetSeconds)
    {
        if (filesInSequence.Count == 0)
            return null;

        if (double.IsNaN(bookOffsetSeconds) || double.IsInfinity(bookOffsetSeconds))
            return null;

        if (bookOffsetSeconds < 0)
            return null;

        // Intervals are half-open: a file covers [StartOffset, StartOffset + Duration).
        // So an offset landing exactly on a boundary belongs to the file that starts there.
        AudioFile? match = null;
        foreach (var file in filesInSequence)
        {
            if (file.StartOffsetSeconds > bookOffsetSeconds)
                break;

            match = file;
        }

        // Offset falls before the first file begins. Only reachable if the timeline
        // does not start at zero, which the scanner's contiguity invariant forbids.
        if (match is null)
            return null;

        var offsetInFile = bookOffsetSeconds - match.StartOffsetSeconds;

        // Past the end of the matched file. Either the offset is past the end of the book,
        // or the timeline has a gap the scanner should have prevented. Both are out of range
        // here rather than clamped, so callers can surface the problem instead of guessing.
        if (offsetInFile >= match.DurationSeconds)
            return null;

        return new TimelineLocation(match, offsetInFile);
    }
}