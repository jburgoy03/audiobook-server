using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookServer.Api.Endpoints;
using AudiobookServer.Core.Entities;
using AudiobookServer.Core.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookServer.Tests.Integration;

/// <summary>
/// Title and author overrides from the admin page. Non-admins (403, cookie and bearer)
/// and anonymous callers (401) are covered by the route table in <see cref="AdminTests"/>.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class AdminBookTests(ApiFixture api)
{
    private sealed record BookView(Guid Id, string Title, string? Author, string? Description = null, string? DescriptionSource = null);

    [Fact]
    public async Task An_override_survives_a_forced_rescan_and_clearing_it_restores_the_scanned_value()
    {
        // A real folder with a real (silent) mp3 and no tags, so the scanner takes the
        // title and author from the folder name, as it does for many real downloads.
        var root = Directory.CreateTempSubdirectory("audiobook-override").FullName;
        var folder = Path.Combine(root, "Scanned Title - Scanned Author");
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "01.mp3"), SilentMp3(frames: 100));

        var libraryId = await LibraryAsync(root);
        var first = await ScanAsync(libraryId, force: false);
        Assert.True(first.BooksAdded == 1, $"The scan should add the book (needs ffprobe on PATH). Report: {first}");

        Guid bookId;
        await using (var db = api.NewDbContext())
            bookId = await db.Books.Where(b => b.LibraryId == libraryId).Select(b => b.Id).SingleAsync();

        using var admin = await api.CookieClientAsync();

        var set = await admin.PutAsJsonAsync($"/api/admin/books/{bookId}/metadata",
            new { title = "  Fixed Title ", author = "Fixed Author" });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var row = (await set.Content.ReadFromJsonAsync<AdminBookResponse>(ApiFixture.Json))!;
        Assert.Equal("Fixed Title", row.Title);
        Assert.Equal("Fixed Title", row.TitleOverride);
        Assert.Equal("Scanned Title", row.ScannedTitle);
        Assert.Equal("Scanned Author", row.ScannedAuthor);

        var rescan = await ScanAsync(libraryId, force: true);
        Assert.Equal(1, rescan.BooksUpdated);

        // The scanner rewrote its own columns and left the override alone.
        await using (var db = api.NewDbContext())
        {
            var stored = await db.Books.AsNoTracking().SingleAsync(b => b.Id == bookId);
            Assert.Equal("Scanned Title", stored.Title);
            Assert.Equal("Fixed Title", stored.TitleOverride);
            Assert.Equal("Fixed Author", stored.AuthorOverride);
        }

        var detail = await admin.GetFromJsonAsync<BookView>($"/api/books/{bookId}", ApiFixture.Json);
        Assert.Equal(("Fixed Title", "Fixed Author"), (detail!.Title, detail.Author));
        var listed = (await admin.GetFromJsonAsync<List<BookView>>("/api/books", ApiFixture.Json))!
            .Single(b => b.Id == bookId);
        Assert.Equal(("Fixed Title", "Fixed Author"), (listed.Title, listed.Author));

        // Clearing: an empty field and a missing one both mean "no override".
        var clear = await admin.PutAsJsonAsync($"/api/admin/books/{bookId}/metadata", new { title = "" });
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        var cleared = await admin.GetFromJsonAsync<BookView>($"/api/books/{bookId}", ApiFixture.Json);
        Assert.Equal(("Scanned Title", "Scanned Author"), (cleared!.Title, cleared.Author));
    }

    [Fact]
    public async Task Listeners_see_the_override_but_cannot_set_it()
    {
        var bookId = await api.CreateBookAsync(60, libraryId: api.PublicLibraryId);
        using var admin = await api.BearerClientAsync();
        using var visitor = await api.CookieClientAsync(ApiFixture.VisitorUsername, ApiFixture.VisitorPassword);

        var refused = await visitor.PutAsJsonAsync($"/api/admin/books/{bookId}/metadata", new { title = "Mine" });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var set = await admin.PutAsJsonAsync($"/api/admin/books/{bookId}/metadata", new { title = "Shown To All" });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        var seen = await visitor.GetFromJsonAsync<BookView>($"/api/books/{bookId}", ApiFixture.Json);
        Assert.Equal("Shown To All", seen!.Title);
    }

    [Fact]
    public async Task Saving_the_scanned_value_stores_no_override()
    {
        var bookId = await api.CreateBookAsync(60);
        string scanned;
        await using (var db = api.NewDbContext())
            scanned = await db.Books.Where(b => b.Id == bookId).Select(b => b.Title).SingleAsync();

        using var admin = await api.CookieClientAsync();
        var set = await admin.PutAsJsonAsync($"/api/admin/books/{bookId}/metadata", new { title = scanned, author = "" });
        var row = (await set.Content.ReadFromJsonAsync<AdminBookResponse>(ApiFixture.Json))!;

        Assert.Null(row.TitleOverride);
        Assert.Null(row.AuthorOverride);
    }

    [Fact]
    public async Task An_unknown_book_is_404()
    {
        using var admin = await api.CookieClientAsync();
        var response = await admin.PutAsJsonAsync($"/api/admin/books/{Guid.NewGuid()}/metadata", new { title = "X" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Too_long_is_400_and_changes_nothing()
    {
        var bookId = await api.CreateBookAsync(60);
        using var admin = await api.CookieClientAsync();

        var response = await admin.PutAsJsonAsync($"/api/admin/books/{bookId}/metadata",
            new { title = "Fine", author = new string('x', 301) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // Every error is ProblemDetails: the one shape the apps parse.
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.StartsWith("An author can be at most", problem.GetProperty("detail").GetString());
        await using var db = api.NewDbContext();
        Assert.Null(await db.Books.Where(b => b.Id == bookId).Select(b => b.TitleOverride).SingleAsync());
    }

    [Fact]
    public async Task Fetching_a_blurb_searches_by_the_overridden_title_and_listeners_see_it()
    {
        var bookId = await api.CreateBookAsync(60, libraryId: api.PublicLibraryId);
        using var admin = await api.CookieClientAsync();
        await admin.PutAsJsonAsync($"/api/admin/books/{bookId}/metadata", new { title = "Blurbed Book", author = "An Author" });

        var response = await admin.PostAsync($"/api/admin/books/{bookId}/description/fetch", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<FetchBlurbResponse>(ApiFixture.Json))!;
        Assert.True(result.Found);
        Assert.Equal("Blurbed Book", result.MatchedTitle);
        Assert.Equal("Test", result.Book.DescriptionSource);

        using var visitor = await api.CookieClientAsync(ApiFixture.VisitorUsername, ApiFixture.VisitorPassword);
        var seen = await visitor.GetFromJsonAsync<BookView>($"/api/books/{bookId}", ApiFixture.Json);
        Assert.Equal("A blurb for Blurbed Book by An Author, long enough to count.", seen!.Description);
        Assert.Equal("Test", seen.DescriptionSource);
    }

    [Fact]
    public async Task Nothing_found_keeps_the_existing_blurb()
    {
        var bookId = await BookWithBlurbAsync();
        using var admin = await api.CookieClientAsync();

        var result = (await (await admin.PostAsync($"/api/admin/books/{bookId}/description/fetch", null))
            .Content.ReadFromJsonAsync<FetchBlurbResponse>(ApiFixture.Json))!;

        Assert.False(result.Found);
        Assert.Equal("An earlier blurb.", result.Book.Description);
    }

    [Fact]
    public async Task An_unreachable_catalogue_is_502_and_changes_nothing()
    {
        var bookId = await BookWithBlurbAsync();
        using var admin = await api.CookieClientAsync();
        await admin.PutAsJsonAsync($"/api/admin/books/{bookId}/metadata", new { title = "Unreachable" });

        var response = await admin.PostAsync($"/api/admin/books/{bookId}/description/fetch", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        await using var db = api.NewDbContext();
        Assert.Equal("An earlier blurb.", await db.Books.Where(b => b.Id == bookId).Select(b => b.Description).SingleAsync());
    }

    [Fact]
    public async Task Removing_a_blurb_clears_it_and_its_source()
    {
        var bookId = await BookWithBlurbAsync();
        using var admin = await api.CookieClientAsync();

        var response = await admin.DeleteAsync($"/api/admin/books/{bookId}/description");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = (await response.Content.ReadFromJsonAsync<AdminBookResponse>(ApiFixture.Json))!;
        Assert.Null(row.Description);
        Assert.Null(row.DescriptionSource);
    }

    [Fact]
    public async Task Blurb_routes_are_404_for_an_unknown_book()
    {
        using var admin = await api.CookieClientAsync();
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsync($"/api/admin/books/{Guid.NewGuid()}/description/fetch", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.DeleteAsync($"/api/admin/books/{Guid.NewGuid()}/description")).StatusCode);
    }

    private async Task<Guid> BookWithBlurbAsync()
    {
        var bookId = await api.CreateBookAsync(60);
        await using var db = api.NewDbContext();
        await db.Books.Where(b => b.Id == bookId).ExecuteUpdateAsync(s => s
            .SetProperty(b => b.Description, "An earlier blurb.")
            .SetProperty(b => b.DescriptionSource, "Open Library"));
        return bookId;
    }

    [Fact]
    public async Task The_admin_list_includes_private_books()
    {
        using var admin = await api.CookieClientAsync();
        var rows = (await admin.GetFromJsonAsync<List<AdminBookResponse>>("/api/admin/books", ApiFixture.Json))!;
        Assert.Contains(rows, r => r.Id == api.PrivateBookId);
        Assert.Contains(rows, r => r.Id == api.PublicBookId);
    }

    /// <summary>
    /// MPEG-1 Layer III, 128 kbps, 44.1 kHz, mono, no padding: 417-byte frames whose
    /// side information is all zeros, which decodes as silence. Enough frames for
    /// ffprobe to lock on; 100 is about 2.6 seconds.
    /// </summary>
    private static byte[] SilentMp3(int frames)
    {
        const int frameSize = 144 * 128_000 / 44_100; // 417
        var bytes = new byte[frameSize * frames];
        for (var i = 0; i < frames; i++)
        {
            var at = i * frameSize;
            bytes[at] = 0xFF;
            bytes[at + 1] = 0xFB; // sync, MPEG-1, Layer III, no CRC
            bytes[at + 2] = 0x90; // 128 kbps, 44.1 kHz, no padding
            bytes[at + 3] = 0xC0; // mono
        }
        return bytes;
    }

    private async Task<Guid> LibraryAsync(string root)
    {
        await using var db = api.NewDbContext();
        var library = new Library
        {
            Id = Guid.NewGuid(), Name = "Overrides " + Guid.NewGuid().ToString("N")[..6],
            RootPath = root, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Libraries.Add(library);
        await db.SaveChangesAsync();
        return library.Id;
    }

    private async Task<ScanReport> ScanAsync(Guid libraryId, bool force)
    {
        using var scope = api.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ILibraryScanService>().ScanAsync(libraryId, force);
    }
}
