using System.Globalization;
using System.Text.RegularExpressions;
using AudiobookServer.Core.Entities;
using AudiobookServer.Core.Media;

namespace AudiobookServer.Core.Scanning;

/// <summary>A fully described book, not yet attached to a library or persisted.</summary>
public record ScannedBook(
    string Key,
    string Title,
    string? Author,
    string? Narrator,
    double DurationSeconds,
    IReadOnlyList<AudioFile> Files,
    IReadOnlyList<Chapter> Chapters,
    CoverSource? Cover = null);

public interface IBookScanner
{
    /// <summary>
    /// Usually returns one book. A directory whose files disagree about their album
    /// is a collection, and yields one book per album.
    /// </summary>
    Task<IReadOnlyList<ScannedBook>> ScanAsync(
        BookCandidate candidate, string libraryRoot, CancellationToken ct = default);
}

public partial class BookScanner(IMediaProbe probe) : IBookScanner
{
    public async Task<IReadOnlyList<ScannedBook>> ScanAsync(
        BookCandidate candidate, string libraryRoot, CancellationToken ct = default)
    {
        var probed = new List<ProbedFile>();

        foreach (var path in candidate.FilePaths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                probed.Add(await probe.ProbeAsync(path, ct));
            }
            catch (MediaProbeException)
            {
                // One unreadable file shouldn't discard the rest of the book.
            }
        }

        if (probed.Count == 0)
            return [];

        var directoryName = Path.GetFileName(candidate.DirectoryPath);
        var directoryKey = LibraryPaths.Relative(libraryRoot, candidate.DirectoryPath);

        return GroupIntoBooks(probed)
            .Select(group => BuildBook(group, directoryName, directoryKey, libraryRoot))
            .ToList();
    }

    private sealed record BookGroup(string? Album, List<ProbedFile> Files);

    /// <summary>
    /// One directory normally means one book. When the files disagree about which
    /// album they belong to, the tagger is telling us the folder holds a collection,
    /// so each album becomes its own book. Uniform album tags stay one book even when
    /// the titles look like separate volumes — the tags are the assertion we trust.
    /// </summary>
    private static List<BookGroup> GroupIntoBooks(List<ProbedFile> probed)
    {
        var distinctAlbums = probed
            .Select(p => p.Tag("album"))
            .Where(a => a is not null)
            .Select(a => a!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (distinctAlbums <= 1)
            return [new BookGroup(null, probed)];

        return probed
            .GroupBy(p => p.Tag("album") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(g => new BookGroup(g.Key.Length == 0 ? null : g.Key, g.ToList()))
            .ToList();
    }

    private static ScannedBook BuildBook(
        BookGroup group, string directoryName, string directoryKey, string libraryRoot)
    {
        var ordered = OrderFiles(group.Files);

        var files = new List<AudioFile>();
        var chapters = new List<Chapter>();
        var offset = 0d;

        foreach (var (p, index) in ordered.Select((p, i) => (p, i)))
        {
            files.Add(new AudioFile
            {
                Id = Guid.NewGuid(),
                RelativePath = LibraryPaths.Relative(libraryRoot, p.Path),
                Sequence = index,
                StartOffsetSeconds = offset,
                DurationSeconds = p.DurationSeconds,
                HeaderDurationSeconds = p.HeaderDurationSeconds ?? p.DurationSeconds,
                DurationSource = p.DurationSource,
                SizeBytes = p.SizeBytes,
                Bitrate = p.Bitrate,
                Codec = p.Codec ?? p.FormatName,
                MimeType = MimeTypeFor(p.Path),
                FileModifiedAt = SafeLastWrite(p.Path)
            });

            // Embedded chapters shift onto the book timeline. A file with none
            // contributes a single chapter spanning itself.
            if (p.Chapters.Count > 0)
            {
                foreach (var c in p.Chapters)
                {
                    chapters.Add(new Chapter
                    {
                        Id = Guid.NewGuid(),
                        Title = c.Title ?? FallbackChapterTitle(p, chapters.Count),
                        Sequence = chapters.Count,
                        StartOffsetSeconds = offset + c.StartSeconds,
                        EndOffsetSeconds = offset + Math.Min(c.EndSeconds, p.DurationSeconds)
                    });
                }
            }
            else
            {
                chapters.Add(new Chapter
                {
                    Id = Guid.NewGuid(),
                    Title = p.Tag("title") ?? FallbackChapterTitle(p, chapters.Count),
                    Sequence = chapters.Count,
                    StartOffsetSeconds = offset,
                    EndOffsetSeconds = offset + p.DurationSeconds
                });
            }

            offset += p.DurationSeconds;
        }

        var first = ordered[0];
        var album = group.Album ?? first.Tag("album");

        // Usually every file carries the same picture. The first file in playback
        // order that has one is the cover.
        var coverFile = ordered.FirstOrDefault(p => p.Cover is not null);
        var cover = coverFile is null ? null : new CoverSource(coverFile.Path, coverFile.Cover!);

        return new ScannedBook(
            // Split books need distinct keys, since they share a directory.
            Key: group.Album is null ? directoryKey : $"{directoryKey}#{group.Album}",
            Title: CleanTitle(album) ?? directoryName,
            Author: first.Tag("artist") ?? AuthorFromDirectoryName(directoryName),
            Narrator: first.Tag("composer") ?? first.Tag("narrator"),
            DurationSeconds: offset,
            Files: files,
            Chapters: chapters,
            Cover: cover);
    }

    /// <summary>
    /// Track tags win when present, since they're authoritative. Natural sort on
    /// filename is the fallback, and it's what keeps unpadded numbering correct.
    /// </summary>
    private static List<ProbedFile> OrderFiles(List<ProbedFile> probed)
    {
        var withTracks = probed
            .Select(p => (File: p, Track: ParseTrack(p.Tag("track"))))
            .ToList();

        if (withTracks.All(x => x.Track is not null) &&
            withTracks.Select(x => x.Track).Distinct().Count() == withTracks.Count)
        {
            return withTracks.OrderBy(x => x.Track!.Value).Select(x => x.File).ToList();
        }

        return probed
            .OrderBy(p => Path.GetFileName(p.Path), NaturalComparer.Instance)
            .ToList();
    }

    /// <summary>Handles both "3" and "3/21".</summary>
    private static int? ParseTrack(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var head = raw.Split('/')[0].Trim();
        return int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>Strips a leading series name, as in "Gentleman Bastard-The Lies of Locke Lamora".</summary>
    private static string? CleanTitle(string? album)
    {
        if (string.IsNullOrWhiteSpace(album)) return null;

        var match = SeriesPrefix().Match(album);
        return match.Success ? match.Groups["title"].Value.Trim() : album.Trim();
    }

    /// <summary>Last resort when tags carry no artist: "Title by Author" or "Title - Author".</summary>
    private static string? AuthorFromDirectoryName(string directoryName)
    {
        var by = ByPattern().Match(directoryName);
        if (by.Success) return by.Groups["author"].Value.Trim();

        var dash = DashPattern().Match(directoryName);
        if (dash.Success) return dash.Groups["author"].Value.Trim();

        return null;
    }

    private static string FallbackChapterTitle(ProbedFile file, int index) =>
        Path.GetFileNameWithoutExtension(file.Path) is { Length: > 0 } name
            ? name
            : $"Chapter {index + 1}";

    private static string MimeTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".m4b" or ".m4a" or ".aac" => "audio/mp4",
        ".mp3" => "audio/mpeg",
        ".opus" or ".ogg" => "audio/ogg",
        ".flac" => "audio/flac",
        ".wma" => "audio/x-ms-wma",
        _ => "application/octet-stream"
    };

    private static DateTimeOffset SafeLastWrite(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTimeOffset.UtcNow; }
    }

    [GeneratedRegex(@"^(?<series>[^-]{2,40})-(?<title>.+)$")]
    private static partial Regex SeriesPrefix();

    [GeneratedRegex(@"^(?<title>.+?)\s+by\s+(?<author>.+?)$", RegexOptions.IgnoreCase)]
    private static partial Regex ByPattern();

    [GeneratedRegex(@"^(?<title>.+?)\s+-\s+(?<author>.+?)(\s+m4b)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DashPattern();
}