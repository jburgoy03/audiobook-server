using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AudiobookServer.Core.Entities;

namespace AudiobookServer.Core.Scanning;

/// <summary>
/// A fingerprint of a book's audio as clients address it: which file sits at each
/// sequence, and where it falls on the timeline. A client that downloaded the book
/// stores this alongside the files; if the server's value differs, the download no
/// longer matches and must be fetched again.
///
/// Inputs, per file in sequence order: sequence, path within the library, size,
/// modification time (to the millisecond) and duration (to the millisecond). Any
/// change to the bytes moves size or mtime; a rename or reorder moves the path at a
/// sequence; a change to how durations are measured moves the timeline. Each of those
/// invalidates a download, and each errs towards a needless re-download rather than a
/// silently wrong one. Chapters aren't included: clients fetch them with the book's
/// details, and they don't change which bytes sit where.
///
/// Not a content hash: reading every file would make each scan read the whole library.
/// A file rewritten with the same size and an older timestamp preserved (cp -p) goes
/// unnoticed here, as it does in the scanner's own change detection; a forced rescan
/// doesn't help either, since the inputs are unchanged.
/// </summary>
public static class ContentVersion
{
    public static string For(IEnumerable<AudioFile> files)
    {
        var text = new StringBuilder();
        foreach (var f in files.OrderBy(f => f.Sequence))
        {
            text.Append(CultureInfo.InvariantCulture,
                $"{f.Sequence}|{f.RelativePath}|{f.SizeBytes}|{f.FileModifiedAt.ToUnixTimeMilliseconds()}|{Math.Round(f.DurationSeconds * 1000)}\n");
        }

        // 16 hex characters (64 bits): an identifier compared for equality per book,
        // not a security boundary.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }
}
