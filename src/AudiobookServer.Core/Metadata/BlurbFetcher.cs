using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AudiobookServer.Core.Metadata;

/// <summary>A blurb, where it came from, and the record it was taken from (for checking).</summary>
public sealed record Blurb(string Text, string Source, string MatchedTitle, string? MatchedAuthor);

/// <summary>
/// Configuration section "Blurbs". Google Books' anonymous quota is shared by every
/// keyless caller and answers 429 almost at once, so without a key it isn't asked.
/// A free key (Google Cloud console, Books API) allows 1,000 requests a day.
/// </summary>
public sealed record BlurbSettings(string? GoogleBooksApiKey);

public interface IBlurbFetcher
{
    /// <summary>
    /// A blurb for this title and author, or null when no catalogue has one for a
    /// matching title. Throws <see cref="HttpRequestException"/> when a catalogue
    /// can't be reached at all, so "nothing found" and "couldn't look" stay distinct.
    /// </summary>
    Task<Blurb?> FetchAsync(string title, string? author, CancellationToken ct = default);
}

/// <summary>
/// Google Books first when a key is configured (<see cref="BlurbSettings"/>): its
/// descriptions are usually the publisher's back-cover copy, which is what a blurb
/// is. Then Open Library (CC0, the catalogue <c>fetch-covers.py</c> trusts), whose
/// descriptions are volunteer-written and often encyclopedic ("…is a heroic fantasy
/// novel written by…"); without a key it's the only source. From either, text that
/// reads like an encyclopedia entry is skipped (<see cref="BlurbText.LooksEncyclopedic"/>)
/// and the search moves on: blurbs only. Searches
/// the same way as <c>fetch-covers.py</c>: title and author fields first, then a
/// general query, which also matches authors catalogued under another name (Murakami
/// as 村上春樹). A record counts only if its title matches (<see cref="BlurbText.TitleMatches"/>).
///
/// Known weakness: the general query checks the title, not the author, so a common
/// title can land on another author's book. The admin page shows what matched.
/// </summary>
public sealed class BlurbFetcher(HttpClient http, BlurbSettings settings, ILogger<BlurbFetcher> logger) : IBlurbFetcher
{
    public const string OpenLibrary = "Open Library";
    public const string GoogleBooks = "Google Books";

    // Each search looks at this many records for a matching title.
    private const int Candidates = 5;

    public async Task<Blurb?> FetchAsync(string title, string? author, CancellationToken ct = default)
    {
        // A catalogue that couldn't be asked only matters when the other found
        // nothing: then "no blurb" would be a guess, so the failure is reported.
        HttpRequestException? failure = null;

        if (!string.IsNullOrWhiteSpace(settings.GoogleBooksApiKey))
        {
            try
            {
                if (await FromGoogleBooksAsync(title, author, settings.GoogleBooksApiKey, ct) is { } found)
                    return found;
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Google Books search failed for {Title}", title);
                failure = Named(GoogleBooks, ex);
            }
        }

        try
        {
            if (await FromOpenLibraryAsync(title, author, ct) is { } found)
                return found;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Open Library search failed for {Title}", title);
            failure ??= Named(OpenLibrary, ex);
        }

        if (failure is not null)
            throw failure;
        return null;
    }

    private async Task<Blurb?> FromOpenLibraryAsync(string title, string? author, CancellationToken ct)
    {
        var searches = new List<string>();
        const string common = "&fields=key,title,author_name&limit=10&lang=en";
        if (author is not null)
        {
            searches.Add($"https://openlibrary.org/search.json?title={Uri.EscapeDataString(title)}&author={Uri.EscapeDataString(author)}{common}");
            searches.Add($"https://openlibrary.org/search.json?q={Uri.EscapeDataString($"{title} {author}")}{common}");
        }
        else
        {
            searches.Add($"https://openlibrary.org/search.json?title={Uri.EscapeDataString(title)}{common}");
        }

        var tried = new HashSet<string>();
        foreach (var url in searches)
        {
            using var search = await GetJsonAsync(url, ct);
            if (search is null || !search.RootElement.TryGetProperty("docs", out var docs))
                continue;

            var matches = docs.EnumerateArray()
                .Where(d => BlurbText.TitleMatches(title, Text(d, "title")))
                .Take(Candidates);

            foreach (var doc in matches)
            {
                var key = Text(doc, "key");
                if (key is null || !key.StartsWith("/works/", StringComparison.Ordinal) || !tried.Add(key))
                    continue;

                using var work = await GetJsonAsync($"https://openlibrary.org{key}.json", ct);
                var text = work is null ? null : BlurbText.CleanOpenLibrary(BlurbText.OpenLibraryDescription(work.RootElement));
                if (text is not null && !BlurbText.LooksEncyclopedic(text))
                    return new Blurb(text, OpenLibrary, Text(doc, "title")!, FirstOf(doc, "author_name"));
            }
        }

        return null;
    }

    /// <summary>The same failure, saying which catalogue it was ("Google Books: 429 TooManyRequests").</summary>
    private static HttpRequestException Named(string catalogue, HttpRequestException ex) =>
        new(ex.StatusCode is { } status ? $"{catalogue}: {(int)status} {status}" : $"{catalogue}: {ex.Message}", ex, ex.StatusCode);

    /// <summary>
    /// Google's search is touchy: a quoted <c>intitle:"…"</c> phrase with
    /// <c>langRestrict=en</c> found nothing for The Lies of Locke Lamora, which an
    /// unquoted query finds at once. So: unquoted title, the author's surname only
    /// (spellings of initials vary), then a plain query; no language filter, but
    /// English editions first. The title check does the real filtering.
    /// </summary>
    private async Task<Blurb?> FromGoogleBooksAsync(string title, string? author, string apiKey, CancellationToken ct)
    {
        var surname = author?.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        var queries = new List<string>
        {
            $"intitle:{title}" + (surname is null ? "" : $" inauthor:{surname}"),
        };
        if (author is not null)
            queries.Add($"{title} {author}");

        foreach (var q in queries)
        {
            var url = $"https://www.googleapis.com/books/v1/volumes?q={Uri.EscapeDataString(q)}&maxResults=20&printType=books&key={Uri.EscapeDataString(apiKey)}";

            using var search = await GetJsonAsync(url, ct);
            if (search is null || !search.RootElement.TryGetProperty("items", out var items))
                continue;

            var matches = items.EnumerateArray()
                .Select(item => item.TryGetProperty("volumeInfo", out var info) ? info : (JsonElement?)null)
                .OfType<JsonElement>()
                .Where(info => BlurbText.TitleMatches(title, Text(info, "title")))
                .OrderBy(info => Text(info, "language") == "en" ? 0 : 1);

            foreach (var info in matches)
            {
                if (BlurbText.CleanGoogle(Text(info, "description")) is { } text && !BlurbText.LooksEncyclopedic(text))
                    return new Blurb(text, GoogleBooks, Text(info, "title")!, FirstOf(info, "authors"));
            }
        }

        return null;
    }

    /// <summary>Null for a 404 (a missing work); throws for anything else unsuccessful.</summary>
    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? FirstOf(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            && value.GetArrayLength() > 0 && value[0].ValueKind == JsonValueKind.String
            ? value[0].GetString()
            : null;
}
