namespace AudiobookServer.Core.Metadata;

/// <summary>
/// What an admin's edit to a title or author becomes in the override column. Pure, so
/// the rules are unit-tested apart from the endpoint.
/// </summary>
public static class MetadataOverride
{
    public const int MaxTitleLength = 500;
    public const int MaxAuthorLength = 300;

    /// <summary>
    /// Trims the edit, and maps "no override" to null: an empty field, or one that
    /// matches the scanned value exactly. The second is a choice: saving the editor
    /// without touching a field shouldn't pin today's scanned value against a future
    /// retag. The cost is that you can't pin a value that happens to equal the scan.
    /// Too long is a failure (the column's limit), reported rather than truncated.
    /// </summary>
    public static (bool Ok, string? Value) Normalize(string? edited, string? scanned, int maxLength)
    {
        var value = edited?.Trim();
        if (string.IsNullOrEmpty(value))
            return (true, null);
        if (value.Length > maxLength)
            return (false, null);
        if (string.Equals(value, scanned?.Trim(), StringComparison.Ordinal))
            return (true, null);
        return (true, value);
    }
}
