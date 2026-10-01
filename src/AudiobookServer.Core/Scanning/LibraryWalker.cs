using AudiobookServer.Core.Media;

namespace AudiobookServer.Core.Scanning;

/// <summary>
/// A directory that holds audio files, and therefore represents one book (or, when the
/// tags say so, a collection). Images are the readable jpeg and png files beside them,
/// already measured, as cover candidates.
/// </summary>
public record BookCandidate(
    string DirectoryPath,
    IReadOnlyList<string> FilePaths,
    IReadOnlyList<FolderImage>? Images = null)
{
    public IReadOnlyList<FolderImage> FolderImages => Images ?? [];
}

public interface ILibraryWalker
{
    IEnumerable<BookCandidate> FindBooks(string rootPath);
}

public class LibraryWalker : ILibraryWalker
{
    private static readonly HashSet<string> AudioExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".m4b", ".m4a", ".mp3", ".opus", ".ogg", ".flac", ".aac", ".wma" };

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    /// <summary>
    /// A directory containing audio files is one book. Directories containing only
    /// other directories are structure, and get walked through.
    /// </summary>
    public IEnumerable<BookCandidate> FindBooks(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            yield break;

        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            string[] files;
            string[] subdirectories;

            try
            {
                files = Directory.GetFiles(current);
                subdirectories = Directory.GetDirectories(current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // A permission problem on one folder shouldn't abort the whole scan.
                continue;
            }

            foreach (var dir in subdirectories)
                pending.Push(dir);

            var audio = files
                .Where(f => AudioExtensions.Contains(Path.GetExtension(f)))
                .OrderBy(Path.GetFileName, NaturalComparer.Instance)
                .ToList();

            if (audio.Count == 0)
                continue;

            // Only read headers for folders that are books. An unreadable or
            // mislabelled image is skipped, never an error.
            var images = files
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
                .Select(f => (Path: f, Info: ImageHeader.TryReadFile(f)))
                .Where(x => x.Info is not null)
                .Select(x => new FolderImage(x.Path, x.Info!.Format, x.Info.Width, x.Info.Height))
                .ToList();

            yield return new BookCandidate(current, audio, images);
        }
    }
}