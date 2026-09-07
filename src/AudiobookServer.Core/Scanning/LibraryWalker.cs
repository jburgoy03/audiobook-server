namespace AudiobookServer.Core.Scanning;

/// <summary>A directory that holds audio files, and therefore represents one book.</summary>
public record BookCandidate(string DirectoryPath, IReadOnlyList<string> FilePaths);

public interface ILibraryWalker
{
    IEnumerable<BookCandidate> FindBooks(string rootPath);
}

public class LibraryWalker : ILibraryWalker
{
    private static readonly HashSet<string> AudioExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".m4b", ".m4a", ".mp3", ".opus", ".ogg", ".flac", ".aac", ".wma" };

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

            if (audio.Count > 0)
                yield return new BookCandidate(current, audio);
        }
    }
}