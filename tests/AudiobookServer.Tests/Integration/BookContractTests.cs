using System.Net.Http.Json;
using System.Text.Json;
using AudiobookServer.Api.Endpoints;

namespace AudiobookServer.Tests.Integration;

/// <summary>
/// The book JSON is a published contract: an installed Android app parses it, and it
/// can't be redeployed with the server. These pin every field name the clients rely
/// on, so a rename or removal fails here rather than on someone's phone. Adding a
/// field is allowed (clients ignore unknown fields): add it to the expected list.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class BookContractTests(ApiFixture api)
{
    [Fact]
    public async Task Book_list_rows_keep_their_field_names()
    {
        using var client = await api.CookieClientAsync(ApiFixture.VisitorUsername, ApiFixture.VisitorPassword);
        var books = await client.GetFromJsonAsync<JsonElement[]>("/api/books", ApiFixture.Json);
        var row = books!.Single(b => b.GetProperty("id").GetGuid() == api.PublicBookId);

        Assert.Equal(
            ["addedAt", "author", "chapters", "durationSeconds", "files", "hasCover", "id", "title"],
            Names(row));
    }

    [Fact]
    public async Task Book_detail_keeps_its_field_names()
    {
        using var client = await api.CookieClientAsync(ApiFixture.VisitorUsername, ApiFixture.VisitorPassword);
        var book = await client.GetFromJsonAsync<JsonElement>($"/api/books/{api.PublicBookId}", ApiFixture.Json);

        Assert.Equal(
            [
                "author", "chapters", "credit", "description", "descriptionSource", "durationSeconds",
                "files", "hasCover", "id", "narrator", "publishedYear", "subtitle", "title",
            ],
            Names(book));
        Assert.Equal(
            ["durationSeconds", "mimeType", "sequence", "sizeBytes", "startOffsetSeconds"],
            Names(book.GetProperty("files")[0]));
    }

    [Fact]
    public void Chapters_keep_their_field_names()
    {
        // The fixture's book has no chapters, so this checks the record's own
        // serialisation with the same web defaults the API uses.
        var json = JsonSerializer.SerializeToElement(
            new ChapterDto(0, "One", 0, 60), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(["endOffsetSeconds", "sequence", "startOffsetSeconds", "title"], Names(json));
    }

    private static List<string> Names(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList();
}
