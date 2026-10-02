using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AudiobookServer.Core.Metadata;

/// <summary>
/// The pure half of fetching blurbs: turning catalogue text into plain paragraphs,
/// and deciding whether a catalogue record is the book that was searched for.
/// Heuristics, all of them; the admin page shows every blurb, so a bad one can be
/// removed.
/// </summary>
public static partial class BlurbText
{
    /// <summary>Shorter than this after cleaning is a stub ("A novel."), not a blurb.</summary>
    public const int MinLength = 40;

    /// <summary>
    /// Open Library's work description: either a plain string or
    /// <c>{ "type": "/type/text", "value": "..." }</c>, depending on the record's age.
    /// </summary>
    public static string? OpenLibraryDescription(JsonElement work)
    {
        if (!work.TryGetProperty("description", out var description))
            return null;
        return description.ValueKind switch
        {
            JsonValueKind.String => description.GetString(),
            JsonValueKind.Object when description.TryGetProperty("value", out var value)
                && value.ValueKind == JsonValueKind.String => value.GetString(),
            _ => null,
        };
    }

    /// <summary>
    /// Open Library descriptions are Markdown written by volunteers: links, "([source][1])"
    /// credits, reference lines, and often a "----------" rule followed by "Also
    /// contained in" lists. Keeps the prose before the rule, with links reduced to
    /// their text. Null when nothing blurb-sized is left.
    /// </summary>
    public static string? CleanOpenLibrary(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var text = raw.Replace("\r\n", "\n").Replace('\r', '\n');

        var rule = HorizontalRule().Match(text);
        if (rule.Success)
            text = text[..rule.Index];

        text = SourceCredit().Replace(text, "");
        text = ReferenceDefinition().Replace(text, "");
        text = InlineLink().Replace(text, "$1");
        text = ReferenceLink().Replace(text, "$1");
        text = text.Replace("**", "").Replace("__", "");
        text = Emphasis().Replace(text, "$1");

        return Tidy(text);
    }

    /// <summary>Google Books descriptions are HTML fragments: paragraphs, breaks, entities.</summary>
    public static string? CleanGoogle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var text = Paragraph().Replace(raw, "\n\n");
        text = Tag().Replace(text, "");
        text = WebUtility.HtmlDecode(text);
        return Tidy(text);
    }

    /// <summary>
    /// Whether the text is an encyclopedia entry rather than a blurb: "The Name of the
    /// Wind, also called …, is a heroic fantasy novel written by American author …".
    /// Catalogues paste Wikipedia's lead in place of a description, Open Library often
    /// and Google for some editions. Judged on the first sentence only, by phrases a
    /// back-cover never opens with: "written by", "also called/known as", "is a novel
    /// by", a nationality before author/writer/novelist, "was published in". A
    /// heuristic: a blurb that opens that way is lost, and an entry phrased otherwise
    /// gets through (the admin page shows it, to remove).
    /// </summary>
    public static bool LooksEncyclopedic(string text)
    {
        var first = FirstSentence().Match(text);
        return Encyclopedic().IsMatch(first.Success ? first.Value : text);
    }

    /// <summary>
    /// Whether a catalogue title is the searched one. Case, punctuation and "&amp;"
    /// are ignored, and so is a subtitle on either side (after ":", " (" or " - "),
    /// so "Blind Willow, Sleeping Woman: Twenty-Four Stories" matches. A longer title
    /// that merely starts the same way does not: "Dune" is not "Dune Messiah".
    /// </summary>
    public static bool TitleMatches(string searched, string? found)
    {
        if (string.IsNullOrWhiteSpace(found))
            return false;
        var a = NormalizeTitle(searched);
        return a.Length > 0 && a == NormalizeTitle(found);
    }

    private static string NormalizeTitle(string title)
    {
        var main = Subtitle().Split(title)[0];
        var builder = new StringBuilder(main.Length);
        foreach (var c in main.Replace("&", " and ").ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != ' ')
                builder.Append(' ');
        }
        return builder.ToString().Trim();
    }

    private static string? Tidy(string text)
    {
        var paragraphs = BlankLines().Split(text.Trim())
            .Select(p => Spaces().Replace(p, " ").Trim())
            .Where(p => p.Length > 0);
        var result = string.Join("\n\n", paragraphs);
        return result.Length >= MinLength ? result : null;
    }

    [GeneratedRegex(@"^\s*(-{3,}|_{3,}|\*{3,})\s*$", RegexOptions.Multiline)]
    private static partial Regex HorizontalRule();

    [GeneratedRegex(@"\(\s*\[source\](\[\d+\]|\([^)]*\))\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex SourceCredit();

    [GeneratedRegex(@"^\s*\[\d+\]:\s*\S.*$", RegexOptions.Multiline)]
    private static partial Regex ReferenceDefinition();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex InlineLink();

    [GeneratedRegex(@"\[([^\]]+)\]\[\d+\]")]
    private static partial Regex ReferenceLink();

    [GeneratedRegex(@"<\s*(br\s*/?|/p|/div)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex Paragraph();

    // Up to the first full stop followed by a space and a capital, where the stop
    // doesn't end a title (Dr., Mr., Mrs., Ms., St., Jr., Sr.) or an initial (J. R. R.).
    [GeneratedRegex(@"^.*?(?<!\bDr)(?<!\bMr)(?<!\bMrs)(?<!\bMs)(?<!\bSt)(?<!\bJr)(?<!\bSr)(?<!\b[A-Z])[.!?](?=\s+[A-Z""“]|\s*$)", RegexOptions.Singleline)]
    private static partial Regex FirstSentence();

    [GeneratedRegex(
        @"\bwritten by\b" +
        @"|\balso (called|known as)\b" +
        @"|\b(is|was) an? [^.]{0,80}?\b(novel|novella|book|collection|memoir|play)s?\b[^.]{0,40}?\bby\b" +
        @"|\b(American|British|English|Scottish|Irish|Welsh|Canadian|Australian|Japanese|French|Russian|German|Italian|Spanish|Polish|Swedish|Norwegian)\s+(author|writer|novelist|poet)\b" +
        @"|\b(was|were) (first )?published (in|on|by)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Encyclopedic();

    // *Title* or _Title_ around words, not a lone asterisk or snake_case.
    [GeneratedRegex(@"(?<![\w*_])[*_](\S(?:[^*_\n]*\S)?)[*_](?![\w*_])")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@":|\s\(|\s-\s|\s—\s")]
    private static partial Regex Subtitle();
}
