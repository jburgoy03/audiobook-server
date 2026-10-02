using AudiobookServer.Core.Data;
using AudiobookServer.Core.Entities;
using AudiobookServer.Core.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AudiobookServer.Core.Scanning;

public record ScanReport(
    int BooksAdded,
    int BooksUpdated,
    int BooksUnchanged,
    int BooksRemoved,
    int Failures,
    int FilesDurationCorrected)
{
    public int TotalSeen => BooksAdded + BooksUpdated + BooksUnchanged;
}

public interface ILibraryScanService
{
    Task<ScanReport> ScanAsync(Guid libraryId, bool force = false, CancellationToken ct = default);
}

public class LibraryScanService(
    AudiobookDbContext db,
    ILibraryWalker walker,
    IBookScanner scanner,
    ICoverStore covers,
    ILogger<LibraryScanService> logger) : ILibraryScanService
{
    // A file counts as corrected when its header duration missed by more than a few
    // mp3 frames (~26ms each). A correctly padded CBR file lands within one frame.
    private const double FileCorrectionThresholdSeconds = 0.1;

    // Below this, a book's total correction isn't worth a warning.
    private const double BookWarningThresholdSeconds = 1.0;

    public async Task<ScanReport> ScanAsync(
        Guid libraryId, bool force = false, CancellationToken ct = default)
    {
        var library = await db.Libraries.FirstOrDefaultAsync(l => l.Id == libraryId, ct)
            ?? throw new InvalidOperationException($"Library {libraryId} not found.");

        library.LastScanStartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        int added = 0, updated = 0, unchanged = 0, removed = 0, failures = 0, corrected = 0;

        foreach (var candidate in walker.FindBooks(library.RootPath))
        {
            ct.ThrowIfCancellationRequested();

            var directoryKey = LibraryPaths.Relative(library.RootPath, candidate.DirectoryPath);
            var splitPrefix = directoryKey + "#";

            try
            {
                // AsNoTracking throughout: the scan only reads these to compare against
                // disk, and every write below goes through ExecuteUpdate/ExecuteDelete.
                // Nothing is tracked, so EF cannot generate statements we did not ask for.
                var existing = await db.Books
                    .AsNoTracking()
                    .Include(b => b.Files)
                    .Where(b => b.LibraryId == libraryId &&
                                (b.RelativePath == directoryKey || b.RelativePath.StartsWith(splitPrefix)))
                    .ToListAsync(ct);

                if (!force && existing.Count > 0 && !NeedsRescan(existing, candidate, library.RootPath))
                {
                    unchanged += existing.Count;
                    continue;
                }

                var scanned = await scanner.ScanAsync(candidate, library.RootPath, ct);

                if (scanned.Count == 0)
                {
                    logger.LogWarning("No readable audio in {Path}", candidate.DirectoryPath);
                    failures++;
                    continue;
                }

                foreach (var book in scanned)
                {
                    corrected += ReportDurationCorrections(book);

                    var match = existing.FirstOrDefault(b => b.RelativePath == book.Key);

                    // The book ID is settled before anything is written, because the
                    // cover file is named after it.
                    var bookId = match?.Id ?? Guid.NewGuid();
                    var coverPath = await SaveCoverAsync(bookId, book, ct);

                    if (match is null)
                    {
                        db.Books.Add(BuildBook(book, libraryId, bookId, coverPath));
                        await db.SaveChangesAsync(ct);
                        db.ChangeTracker.Clear();
                        added++;
                    }
                    else
                    {
                        await ReplaceContentsAsync(match.Id, book, coverPath, ct);
                        updated++;
                    }
                }

                // Anything previously stored for this directory that the scan no longer
                // produces - most often the single book a folder held before it split.
                var staleIds = existing
                    .Where(b => scanned.All(s => s.Key != b.RelativePath))
                    .Select(b => b.Id)
                    .ToList();

                if (staleIds.Count > 0)
                {
                    removed += await db.Books
                        .Where(b => staleIds.Contains(b.Id))
                        .ExecuteDeleteAsync(ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad book should not end the scan of the whole library.
                logger.LogError(ex, "Failed to scan {Path}", candidate.DirectoryPath);
                failures++;
                db.ChangeTracker.Clear();
            }
        }

        var libraryToFinish = await db.Libraries.FirstAsync(l => l.Id == libraryId, ct);
        libraryToFinish.LastScanCompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var report = new ScanReport(added, updated, unchanged, removed, failures, corrected);
        logger.LogInformation(
            "Scanned {Library}: {Added} added, {Updated} updated, {Unchanged} unchanged, " +
            "{Removed} removed, {Failures} failed, {Corrected} file durations corrected",
            libraryToFinish.Name, added, updated, unchanged, removed, failures, corrected);

        return report;
    }

    /// <summary>
    /// Logs how far packet-counted durations moved from the header estimates, and
    /// returns how many files moved meaningfully. One warning per book, not per file:
    /// a 92-file book with a bad encoder should produce one line, not 92.
    ///
    /// The book total is a signed sum because that is the error a listener feels -
    /// how far the last file's offset was off.
    /// </summary>
    private int ReportDurationCorrections(ScannedBook book)
    {
        var counted = book.Files
            .Where(f => f.DurationSource == DurationSource.PacketCount)
            .ToList();

        var corrected = counted
            .Where(f => Math.Abs(f.DurationSeconds - f.HeaderDurationSeconds) > FileCorrectionThresholdSeconds)
            .ToList();

        foreach (var f in corrected)
        {
            logger.LogDebug(
                "{File}: header {Header:F3}s, counted {Counted:F3}s",
                f.RelativePath, f.HeaderDurationSeconds, f.DurationSeconds);
        }

        var total = counted.Sum(f => f.DurationSeconds - f.HeaderDurationSeconds);
        if (Math.Abs(total) >= BookWarningThresholdSeconds)
        {
            logger.LogWarning(
                "{Title}: header durations were off by {Seconds:+0.0;-0.0}s across {Corrected} of {Files} files; using packet counts",
                book.Title, total, corrected.Count, book.Files.Count);
        }

        // An mp3 still on its header duration means packet counting failed for it.
        var fallbacks = book.Files.Count(f =>
            f.DurationSource == DurationSource.Header &&
            string.Equals(f.Codec, "mp3", StringComparison.OrdinalIgnoreCase));

        if (fallbacks > 0)
        {
            logger.LogWarning(
                "{Title}: packet count failed for {Count} mp3 files; using header durations, which may drift",
                book.Title, fallbacks);
        }

        return corrected.Count;
    }

    /// <summary>
    /// Cheap check that avoids re-probing an unchanged directory. Compares the whole
    /// file set across every book the directory produced, against what is on disk now.
    ///
    /// Images aren't stored, so they're judged by time: an image modified after the
    /// book was last scanned (a cover.jpg just added) triggers a rescan. A copy that
    /// keeps the original timestamp (unzip, cp -p) or a deleted image goes unnoticed;
    /// those need a forced rescan.
    /// </summary>
    private static bool NeedsRescan(List<Book> existing, BookCandidate candidate, string libraryRoot)
    {
        var lastScanned = existing.Min(b => b.LastScannedAt);
        if (lastScanned is null)
            return true;

        foreach (var image in candidate.FolderImages)
        {
            try
            {
                if (File.GetLastWriteTimeUtc(image.Path) > lastScanned.Value)
                    return true;
            }
            catch { return true; }
        }

        var stored = existing.SelectMany(b => b.Files).ToList();

        if (stored.Count != candidate.FilePaths.Count)
            return true;

        // By path within the library, not file name: every disc of a disc set has its
        // own 01.mp3.
        foreach (var path in candidate.FilePaths)
        {
            var relative = LibraryPaths.Relative(libraryRoot, path);
            var match = stored.FirstOrDefault(f => f.RelativePath == relative);

            if (match is null)
                return true;

            DateTimeOffset lastWrite;
            try { lastWrite = File.GetLastWriteTimeUtc(path); }
            catch { return true; }

            // One second of slack: filesystem and database timestamp precision differ.
            if (Math.Abs((lastWrite - match.FileModifiedAt).TotalSeconds) > 1)
                return true;
        }

        return false;
    }

    /// <summary>A failed cover is logged and skipped; it never fails the book.</summary>
    private async Task<string?> SaveCoverAsync(Guid bookId, ScannedBook book, CancellationToken ct)
    {
        try
        {
            return await covers.SaveAsync(bookId, book.Cover, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not extract a cover for {Title}", book.Title);
            return null;
        }
    }

    private static Book BuildBook(ScannedBook scanned, Guid libraryId, Guid bookId, string? coverPath)
    {
        var book = new Book
        {
            Id = bookId,
            CoverPath = coverPath,
            LibraryId = libraryId,
            Title = scanned.Title,
            Author = scanned.Author,
            Narrator = scanned.Narrator,
            RelativePath = scanned.Key,
            DurationSeconds = scanned.DurationSeconds,
            AddedAt = DateTimeOffset.UtcNow,
            LastScannedAt = DateTimeOffset.UtcNow
        };

        foreach (var f in scanned.Files) { f.BookId = book.Id; book.Files.Add(f); }
        foreach (var c in scanned.Chapters) { c.BookId = book.Id; book.Chapters.Add(c); }

        return book;
    }

    /// <summary>
    /// Updates a book in place and replaces its files and chapters.
    ///
    /// Everything here is direct SQL rather than change-tracked entities. Files and
    /// chapters are derived data with no stable identity, so replacing them wholesale
    /// is correct - but doing it by clearing navigation collections makes EF try to
    /// re-parent the old rows instead of deleting them. Deleting children by foreign
    /// key sidesteps that, and keeping the book row preserves its ID so playback
    /// positions survive a rescan.
    /// </summary>
    private async Task ReplaceContentsAsync(Guid bookId, ScannedBook scanned, string? coverPath, CancellationToken ct)
    {
        await db.Books
            .Where(b => b.Id == bookId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Title, scanned.Title)
                .SetProperty(b => b.Author, scanned.Author)
                .SetProperty(b => b.Narrator, scanned.Narrator)
                .SetProperty(b => b.DurationSeconds, scanned.DurationSeconds)
                .SetProperty(b => b.CoverPath, coverPath)
                .SetProperty(b => b.LastScannedAt, DateTimeOffset.UtcNow), ct);

        await db.AudioFiles.Where(f => f.BookId == bookId).ExecuteDeleteAsync(ct);
        await db.Chapters.Where(c => c.BookId == bookId).ExecuteDeleteAsync(ct);

        foreach (var f in scanned.Files) f.BookId = bookId;
        foreach (var c in scanned.Chapters) c.BookId = bookId;

        db.AudioFiles.AddRange(scanned.Files);
        db.Chapters.AddRange(scanned.Chapters);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }
}