using System.Text.RegularExpressions;

namespace AudiobookServer.Core.Media;

/// <summary>A candidate cover, and its size in pixels (0 when unknown).</summary>
public abstract record CoverSource(int Width, int Height)
{
    public long Pixels => (long)Width * Height;
}

/// <summary>A picture stream inside an audio file. Extracted with ffmpeg.</summary>
public sealed record EmbeddedCover(string AudioPath, ProbedCover Picture)
    : CoverSource(Picture.Width, Picture.Height);

/// <summary>An image file in the book's folder (cover.jpg, folder.jpg, ...). Copied as is.</summary>
public sealed record FolderImage(string Path, string Format, int Width, int Height)
    : CoverSource(Width, Height);

/// <summary>
/// Picks one cover for a book from its folder's images and its embedded picture.
///
/// The rule is "largest wins". Embedded art is often a small thumbnail the encoder
/// carried along, while a cover.jpg beside the files is usually the full-size scan,
/// and LibriVox ships its covers only as separate files. Size is the one signal that
/// works across both without trusting anyone's naming.
///
/// Two heuristics, both deliberate and both fallible:
/// - Images named like the back of a box or a disc (back, cd, disc, inlay, inside,
///   booklet, spine) are never picked: a high-resolution back.jpg would otherwise win.
///   A front cover that happens to be called "back" loses; that seems rarer.
/// - In a folder that holds a collection (split into several books by album tag), the
///   folder's images describe the collection, not each book. A book with its own
///   embedded art keeps it; folder images only fill in for books with none.
///
/// Ties go to an image named cover/folder/front, then any other folder image, then
/// the embedded one, then file name order, so the result never depends on enumeration order.
/// </summary>
public static partial class CoverSelector
{
    public static CoverSource? Choose(
        IReadOnlyList<FolderImage> folderImages, EmbeddedCover? embedded, bool folderIsCollection)
    {
        if (folderIsCollection && embedded is not null)
            return embedded;

        var candidates = folderImages
            .Where(i => !NotAFrontCover().IsMatch(System.IO.Path.GetFileNameWithoutExtension(i.Path)))
            .Cast<CoverSource>()
            .ToList();

        if (embedded is not null)
            candidates.Add(embedded);

        return candidates
            .OrderByDescending(c => c.Pixels)
            .ThenBy(Rank)
            .ThenBy(c => c is FolderImage f ? System.IO.Path.GetFileName(f.Path) : "", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static int Rank(CoverSource c) => c switch
    {
        FolderImage f when FrontCoverName().IsMatch(System.IO.Path.GetFileNameWithoutExtension(f.Path)) => 0,
        FolderImage => 1,
        _ => 2,
    };

    // Whole words only, so "Backstory.jpg" or "Cdrom" titles aren't caught by accident.
    [GeneratedRegex(@"(^|[\s_.\-])(back|cd\d*|disc\d*|disk\d*|inlay|inside|booklet|spine)($|[\s_.\-])", RegexOptions.IgnoreCase)]
    private static partial Regex NotAFrontCover();

    [GeneratedRegex(@"^(cover|folder|front)$", RegexOptions.IgnoreCase)]
    private static partial Regex FrontCoverName();
}
