using System.Diagnostics;

namespace AudiobookServer.Core.Media;

public interface ICoverStore
{
    /// <summary>
    /// Stores the cover for a book (extracted from audio, or copied from a folder image)
    /// and returns the stored file name, relative to the cover directory. Null source:
    /// the book has no cover, and any stored one is removed.
    /// </summary>
    Task<string?> SaveAsync(Guid bookId, CoverSource? source, CancellationToken ct = default);

    /// <summary>Absolute path for a stored file name, or null if it is not inside the cover directory.</summary>
    string? Resolve(string fileName);

    /// <summary>
    /// A version of the stored cover no larger than <paramref name="size"/> pixels on its
    /// longest side (one of <see cref="CoverThumbnails.Sizes"/>): its absolute path, or
    /// null if one can't be made (the caller serves the original instead).
    /// </summary>
    Task<string?> ThumbnailAsync(string fileName, int size, CancellationToken ct = default);
}

public static class CoverThumbnails
{
    /// <summary>
    /// The sizes clients may ask for, chosen for phones: 320 for list rows and the media
    /// notification, 640 for a two- or three-column grid, 1080 for a full-width cover.
    /// A fixed set, so requests can't fill the disk with one file per arbitrary size.
    /// </summary>
    public static readonly IReadOnlyList<int> Sizes = [320, 640, 1080];
}

/// <summary>
/// Covers are pulled out of the audio once, at scan time, and kept on disk keyed by
/// book ID. Book IDs survive rescans, so the file name is stable; extracting on every
/// request would mean running ffmpeg per image view.
/// </summary>
public class FfmpegCoverStore(string directory, string executable = "ffmpeg") : ICoverStore
{
    private readonly string _directory = Path.GetFullPath(directory);
    private string ThumbnailDirectory => Path.Combine(_directory, "thumbs");

    // A grid's first load asks for dozens of thumbnails at once; two ffmpeg processes at
    // a time is plenty, and leaves the rest of the server responsive.
    private readonly SemaphoreSlim _thumbnailing = new(2);

    public async Task<string?> SaveAsync(Guid bookId, CoverSource? source, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);

        if (source is null)
        {
            DeleteStored(bookId, keep: null);
            DeleteThumbnails(bookId);
            return null;
        }

        var extension = source switch
        {
            FolderImage { Format: "png" } => "png",
            FolderImage => "jpg",
            EmbeddedCover { Picture.Codec: var c } when c.Equals("png", StringComparison.OrdinalIgnoreCase) => "png",
            _ => "jpg",
        };

        var fileName = $"{bookId}.{extension}";
        var finalPath = Path.Combine(_directory, fileName);
        var tempPath = finalPath + ".tmp";

        try
        {
            switch (source)
            {
                case FolderImage image:
                    // Already a jpeg or png (ImageHeader checked): copied byte for byte.
                    File.Copy(image.Path, tempPath, overwrite: true);
                    break;
                case EmbeddedCover embedded:
                    await ExtractAsync(embedded, tempPath, ct);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown cover source.");
            }

            // Write-then-move, so a request never sees a half-written image.
            File.Move(tempPath, finalPath, overwrite: true);
            DeleteStored(bookId, keep: fileName);
            DeleteThumbnails(bookId);
            return fileName;
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private async Task ExtractAsync(EmbeddedCover source, string tempPath, CancellationToken ct)
    {
        // jpeg and png are kept byte for byte. Anything else is rare enough that
        // re-encoding it to jpeg beats teaching every client another format.
        var codecArgs = source.Picture.Codec.ToLowerInvariant() switch
        {
            "mjpeg" or "png" => new[] { "-c:v", "copy" },
            _ => new[] { "-c:v", "mjpeg", "-q:v", "2" }
        };

        var args = new List<string> { "-v", "error", "-y", "-i", source.AudioPath, "-map", $"0:{source.Picture.StreamIndex}" };
        args.AddRange(codecArgs);
        // image2 with -update 1 writes a single image to a plain file name. The format
        // is explicit because the .tmp extension tells ffmpeg nothing.
        args.AddRange(["-frames:v", "1", "-update", "1", "-f", "image2", tempPath]);

        var (exitCode, stderr) = await RunAsync(args, ct);
        if (exitCode != 0 || !File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
            throw new MediaProbeException($"ffmpeg exited {exitCode} extracting cover from '{source.AudioPath}': {stderr.Trim()}");
    }

    /// <summary>
    /// Made on first request and kept, rather than at scan time: existing books get them
    /// without a rescan, and a scan stays as fast as it was. Storing a new cover deletes
    /// the book's thumbnails (see SaveAsync); the timestamp check below is a second line,
    /// for a cover replaced some other way.
    /// </summary>
    public async Task<string?> ThumbnailAsync(string fileName, int size, CancellationToken ct = default)
    {
        if (!CoverThumbnails.Sizes.Contains(size))
            throw new ArgumentOutOfRangeException(nameof(size), size, "Not a thumbnail size.");

        var source = Resolve(fileName);
        if (source is null || !File.Exists(source))
            return null;

        // Already that small: the original is the thumbnail. Never upscaled or re-encoded.
        if (ImageHeader.TryReadFile(source) is { } info && Math.Max(info.Width, info.Height) <= size)
            return source;

        Directory.CreateDirectory(ThumbnailDirectory);
        var thumbnail = Path.Combine(ThumbnailDirectory, $"{Path.GetFileNameWithoutExtension(fileName)}-{size}.jpg");
        if (IsCurrent(thumbnail, source))
            return thumbnail;

        await _thumbnailing.WaitAsync(ct);
        var tempPath = $"{thumbnail}.{Guid.NewGuid():N}.tmp";
        try
        {
            // Another request may have made it while this one waited.
            if (IsCurrent(thumbnail, source))
                return thumbnail;

            // Longest side to `size`, keeping the aspect ratio, never enlarging.
            var scale = $"scale=w='min(iw,{size})':h='min(ih,{size})':force_original_aspect_ratio=decrease";
            var (exitCode, _) = await RunAsync(
                ["-v", "error", "-y", "-i", source, "-vf", scale, "-frames:v", "1", "-update", "1",
                 "-c:v", "mjpeg", "-q:v", "4", "-f", "image2", tempPath], ct);

            if (exitCode != 0 || !File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                return null;

            File.Move(tempPath, thumbnail, overwrite: true);
            return thumbnail;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ffmpeg missing or unhappy with the image: the original still works.
            return null;
        }
        finally
        {
            _thumbnailing.Release();
            TryDelete(tempPath);
        }
    }

    private static bool IsCurrent(string thumbnail, string source) =>
        File.Exists(thumbnail) && File.GetLastWriteTimeUtc(thumbnail) >= File.GetLastWriteTimeUtc(source);

    private async Task<(int ExitCode, string Stderr)> RunAsync(IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start {executable}.");

        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stderr = await stderrTask;
        await stdoutTask;
        return (process.ExitCode, stderr);
    }

    public string? Resolve(string fileName)
    {
        var full = Path.GetFullPath(Path.Combine(_directory, fileName));
        var prefix = _directory.EndsWith(Path.DirectorySeparatorChar) ? _directory : _directory + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.Ordinal) ? full : null;
    }

    /// <summary>Removes this book's other stored covers, e.g. an old .png after the cover became a .jpg.</summary>
    private void DeleteStored(Guid bookId, string? keep)
    {
        foreach (var path in Directory.EnumerateFiles(_directory, $"{bookId}.*"))
        {
            if (keep is null || !string.Equals(Path.GetFileName(path), keep, StringComparison.OrdinalIgnoreCase))
                TryDelete(path);
        }
    }

    private void DeleteThumbnails(Guid bookId)
    {
        if (!Directory.Exists(ThumbnailDirectory))
            return;
        foreach (var path in Directory.EnumerateFiles(ThumbnailDirectory, $"{bookId}-*"))
            TryDelete(path);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
