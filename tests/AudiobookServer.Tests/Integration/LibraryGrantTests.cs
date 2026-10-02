using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookServer.Api.Auth;
using AudiobookServer.Core.Entities;

namespace AudiobookServer.Tests.Integration;

/// <summary>
/// Grants from the admin page: PUT /api/admin/users/{id}/libraries replaces an
/// account's set. A grant shows the library's books on the next request and a revoke
/// hides them again, without signing anyone out. The visibility matrix covers each
/// endpoint; this covers the round trip. (Non-admins get 403: AdminTests.)
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class LibraryGrantTests(ApiFixture api)
{
    private const string Passphrase = "a granted test passphrase";

    [Fact]
    public async Task A_grant_shows_a_private_library_and_a_revoke_hides_it_in_the_same_session()
    {
        var (libraryId, bookId) = await PrivateLibraryWithBookAsync();
        var (userId, name) = await UserAsync();

        using var admin = await api.CookieClientAsync();
        using var listener = await api.CookieClientAsync(name, Passphrase);

        Assert.Equal(HttpStatusCode.NotFound, (await listener.GetAsync($"/api/books/{bookId}")).StatusCode);

        var grant = await admin.PutAsJsonAsync($"/api/admin/users/{userId}/libraries", new { libraryIds = new[] { libraryId } });
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);

        // Same cookie: no sign-out needed.
        Assert.Equal(HttpStatusCode.OK, (await listener.GetAsync($"/api/books/{bookId}")).StatusCode);
        var listed = await listener.GetFromJsonAsync<JsonElement[]>("/api/books", ApiFixture.Json);
        Assert.Contains(listed!, b => b.GetProperty("id").GetGuid() == bookId);

        var users = await admin.GetFromJsonAsync<AccountSummary[]>("/api/admin/users", ApiFixture.Json);
        Assert.Equal([libraryId], Assert.Single(users!, u => u.Id == userId).LibraryIds);

        var revoke = await admin.PutAsJsonAsync($"/api/admin/users/{userId}/libraries", new { libraryIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await listener.GetAsync($"/api/books/{bookId}")).StatusCode);
    }

    [Fact]
    public async Task Setting_the_same_grants_twice_changes_nothing()
    {
        var (libraryId, _) = await PrivateLibraryWithBookAsync();
        var (userId, _) = await UserAsync();
        using var admin = await api.CookieClientAsync();

        for (var i = 0; i < 2; i++)
        {
            var response = await admin.PutAsJsonAsync($"/api/admin/users/{userId}/libraries",
                new { libraryIds = new[] { libraryId, libraryId } });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        await using var db = api.NewDbContext();
        Assert.Equal(1, db.LibraryGrants.Count(g => g.UserId == userId));
    }

    [Fact]
    public async Task An_unknown_library_is_400_and_changes_nothing()
    {
        var (libraryId, _) = await PrivateLibraryWithBookAsync();
        var (userId, _) = await UserAsync();
        using var admin = await api.CookieClientAsync();

        await admin.PutAsJsonAsync($"/api/admin/users/{userId}/libraries", new { libraryIds = new[] { libraryId } });
        var response = await admin.PutAsJsonAsync($"/api/admin/users/{userId}/libraries",
            new { libraryIds = new[] { Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var db = api.NewDbContext();
        Assert.Equal([libraryId], db.LibraryGrants.Where(g => g.UserId == userId).Select(g => g.LibraryId).ToList());
    }

    [Fact]
    public async Task An_unknown_user_is_404()
    {
        using var admin = await api.CookieClientAsync();
        var response = await admin.PutAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/libraries",
            new { libraryIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Its own private library and book, so grants here can't disturb other tests.</summary>
    private async Task<(Guid LibraryId, Guid BookId)> PrivateLibraryWithBookAsync()
    {
        var library = new Library
        {
            Id = Guid.NewGuid(),
            Name = "Grant " + Guid.NewGuid().ToString("N")[..8],
            RootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await using (var db = api.NewDbContext())
        {
            db.Libraries.Add(library);
            await db.SaveChangesAsync();
        }
        var bookId = await api.CreateBookAsync(60, libraryId: library.Id);
        return (library.Id, bookId);
    }

    private async Task<(Guid Id, string Name)> UserAsync()
    {
        var name = "grantee-" + Guid.NewGuid().ToString("N")[..8];
        var user = await api.CreateUserAsync(name, Passphrase);
        return (user.Id, name);
    }
}
