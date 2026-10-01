namespace AudiobookServer.Core.Scanning;

/// <summary>
/// Relative paths as stored in the database. They always use '/' as the separator,
/// whichever OS ran the scan, so a library scanned on Windows and one scanned on Linux
/// produce the same keys, and books keep their identity (and their playback positions).
/// </summary>
public static class LibraryPaths
{
    /// <summary><see cref="Path.GetRelativePath"/>, with '/' separators on every OS.</summary>
    public static string Relative(string root, string path) => Normalize(Path.GetRelativePath(root, path));

    /// <summary>
    /// Replaces this OS's separator with '/'. On Linux that is a no-op, which matters:
    /// '\' is a legal filename character there, so it must never be rewritten.
    /// </summary>
    public static string Normalize(string relativePath) =>
        Path.DirectorySeparatorChar == '/'
            ? relativePath
            : relativePath.Replace(Path.DirectorySeparatorChar, '/');
}
