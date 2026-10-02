using System.Text.RegularExpressions;
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

public partial class LibraryWalker : ILibraryWalker
{
    private static readonly HashSet<string> AudioExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".m4b", ".m4a", ".mp3", ".opus", ".ogg", ".flac", ".aac", ".wma" };

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    /// <summary>
    /// A directory containing audio files is one book. Directories containing only
    /// other directories are structure, and get walked through, with one exception:
    /// a directory whose subdirectories are all disc folders ("cd 01 of 11", "disc 3")
    /// is one book spread across discs, the way ripped CD sets arrive.
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

            if (!TryList(current, out var files, out var subdirectories))
                continue;

            var audio = AudioIn(files);

            if (audio.Count == 0 && IsDiscSet(subdirectories))
            {
                if (DiscSetCandidate(current, files, subdirectories) is { } discSet)
                    yield return discSet;
                continue;
            }

            foreach (var dir in subdirectories)
                pending.Push(dir);

            if (audio.Count == 0)
                continue;

            yield return new BookCandidate(current, audio, ImagesIn(files));
        }
    }

    /// <summary>
    /// Every subdirectory is a disc folder. All, not some: a book folder with "disc 1",
    /// "disc 2" and "Extras" isn't a clean set, so it keeps today's behaviour (each
    /// folder its own book) rather than guessing. "Part" is deliberately not a disc
    /// word: "Part 1" folders are too often separate books.
    /// </summary>
    private static bool IsDiscSet(string[] subdirectories) =>
        subdirectories.Length > 0 &&
        subdirectories.All(d => DiscFolder().IsMatch(Path.GetFileName(d)));

    /// <summary>
    /// One candidate for the whole set: discs in natural order (disc 2 before disc 10),
    /// files by name within each disc. Images from the book folder and every disc
    /// compete to be the cover. Null when no disc holds audio.
    /// </summary>
    private static BookCandidate? DiscSetCandidate(string bookDirectory, string[] files, string[] discs)
    {
        var audio = new List<string>();
        var images = new List<FolderImage>(ImagesIn(files));

        foreach (var disc in discs.OrderBy(Path.GetFileName, NaturalComparer.Instance))
        {
            if (!TryList(disc, out var discFiles, out _))
                continue;
            audio.AddRange(AudioIn(discFiles));
            images.AddRange(ImagesIn(discFiles));
        }

        return audio.Count == 0 ? null : new BookCandidate(bookDirectory, audio, images);
    }

    private static bool TryList(string directory, out string[] files, out string[] subdirectories)
    {
        try
        {
            files = Directory.GetFiles(directory);
            subdirectories = Directory.GetDirectories(directory);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // A permission problem on one folder shouldn't abort the whole scan.
            files = [];
            subdirectories = [];
            return false;
        }
    }

    private static List<string> AudioIn(string[] files) =>
        files
            .Where(f => AudioExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(Path.GetFileName, NaturalComparer.Instance)
            .ToList();

    /// <summary>
    /// Only called for folders that are books. An unreadable or mislabelled image is
    /// skipped, never an error.
    /// </summary>
    private static List<FolderImage> ImagesIn(string[] files) =>
        files
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
            .Select(f => (Path: f, Info: ImageHeader.TryReadFile(f)))
            .Where(x => x.Info is not null)
            .Select(x => new FolderImage(x.Path, x.Info!.Format, x.Info.Width, x.Info.Height))
            .ToList();

    /// <summary>"cd 01 of 11", "Disc 3", "disk_2", "CD1".</summary>
    [GeneratedRegex(@"^(cd|disc|disk)[\s._-]*\d+(\s*of\s*\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DiscFolder();
}
