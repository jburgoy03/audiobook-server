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
}

/// <summary>
/// Covers are pulled out of the audio once, at scan time, and kept on disk keyed by
/// book ID. Book IDs survive rescans, so the file name is stable; extracting on every
/// request would mean running ffmpeg per image view.
/// </summary>
public class FfmpegCoverStore(string directory, string executable = "ffmpeg") : ICoverStore
{
    private readonly string _directory = Path.GetFullPath(directory);

    public async Task<string?> SaveAsync(Guid bookId, CoverSource? source, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);

        if (source is null)
        {
            DeleteStored(bookId, keep: null);
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

        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in new[] { "-v", "error", "-y", "-i", source.AudioPath, "-map", $"0:{source.Picture.StreamIndex}" })
            psi.ArgumentList.Add(arg);
        foreach (var arg in codecArgs)
            psi.ArgumentList.Add(arg);
        // image2 with -update 1 writes a single image to a plain file name. The format
        // is explicit because the .tmp extension tells ffmpeg nothing.
        foreach (var arg in new[] { "-frames:v", "1", "-update", "1", "-f", "image2", tempPath })
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start {executable}.");

        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stderr = await stderrTask;
        await stdoutTask;

        if (process.ExitCode != 0 || !File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
            throw new MediaProbeException($"ffmpeg exited {process.ExitCode} extracting cover from '{source.AudioPath}': {stderr.Trim()}");
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

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
