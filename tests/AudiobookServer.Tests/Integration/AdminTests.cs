using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AudiobookServer.Api.Auth;
using AudiobookServer.Api.Endpoints;
using AudiobookServer.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookServer.Tests.Integration;

/// <summary>
/// Who may manage the server. A listener reaching an admin endpoint would see server
/// paths and could start scans, and nothing about that failure looks broken from the
/// outside, so every admin route is pinned here for both kinds of client.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class AdminTests(ApiFixture api)
{
    public static TheoryData<string, string> AdminRoutes => new()
    {
        { "GET", "/api/libraries" },
        { "GET", "/api/libraries/{library}" },
        { "POST", "/api/libraries" },
        { "PATCH", "/api/libraries/{library}" },
        { "POST", "/api/libraries/{library}/scan" },
        { "POST", "/api/libraries/{library}/scan?force=true" },
        { "GET", "/api/admin/users" },
        { "POST", "/api/admin/users" },
        { "POST", "/api/admin/users/{user}/reset-password" },
        { "POST", "/api/admin/users/{user}/disable" },
        { "POST", "/api/admin/users/{user}/enable" },
        { "PUT", "/api/admin/users/{user}/libraries" },
    };

    public static TheoryData<string, string, string> AdminRoutesByClient
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var client in new[] { "cookie", "bearer" })
                foreach (var row in AdminRoutes)
                    data.Add(client, (string)row[0], (string)row[1]);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AdminRoutesByClient))]
    public async Task A_signed_in_non_admin_gets_403(string clientKind, string method, string route)
    {
        using var client = clientKind == "cookie"
            ? await api.CookieClientAsync(ApiFixture.VisitorUsername, ApiFixture.VisitorPassword)
            : await api.BearerClientAsync(ApiFixture.VisitorUsername, ApiFixture.VisitorPassword);

        using var response = await client.SendAsync(Request(method, route));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task Anonymous_is_still_401_on_admin_routes(string method, string route)
    {
        using var client = api.CreateClient();
        using var response = await client.SendAsync(Request(method, route));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_admin_reaches_the_library_endpoints()
    {
        using var client = await api.CookieClientAsync();
        using var response = await client.GetAsync("/api/libraries");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_admin_can_make_a_library_public_and_private_again()
    {
        var bookId = await api.CreateBookAsync(60);
        await using (var db = api.NewDbContext())
        {
            // Its own library, so flipping it can't disturb other tests.
            var library = new Library
            {
                Id = Guid.NewGuid(), Name = "Flip", RootPath = Path.GetTempPath(), CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Libraries.Add(library);
            await db.SaveChangesAsync();
            await db.Books.Where(b => b.Id == bookId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.LibraryId, library.Id));

            using var admin = await api.CookieClientAsync();
            using var visitor = await api.CookieClientAsync(ApiFixture.VisitorUsername, ApiFixture.VisitorPassword);

            Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/books/{bookId}")).StatusCode);

            var open = await admin.PatchAsJsonAsync($"/api/libraries/{library.Id}", new { isPublic = true });
            Assert.Equal(HttpStatusCode.OK, open.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync($"/api/books/{bookId}")).StatusCode);

            var close = await admin.PatchAsJsonAsync($"/api/libraries/{library.Id}", new { isPublic = false });
            Assert.Equal(HttpStatusCode.OK, close.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/books/{bookId}")).StatusCode);
        }
    }

    [Fact]
    public async Task Scanning_an_unknown_library_is_404_not_500()
    {
        using var client = await api.BearerClientAsync();
        using var response = await client.PostAsync($"/api/libraries/{Guid.NewGuid()}/scan", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Me_reports_admin_for_the_admin_only()
    {
        using var admin = await api.CookieClientAsync();
        using var visitor = await api.CookieClientAsync(ApiFixture.VisitorUsername, ApiFixture.VisitorPassword);

        var adminMe = await admin.GetFromJsonAsync<CurrentUser>("/api/auth/me", ApiFixture.Json);
        var visitorMe = await visitor.GetFromJsonAsync<CurrentUser>("/api/auth/me", ApiFixture.Json);

        Assert.True(adminMe!.IsAdmin);
        Assert.False(visitorMe!.IsAdmin);
    }

    [Fact]
    public async Task Changing_admin_invalidates_refresh_tokens_and_new_sessions_get_the_new_claim()
    {
        const string name = "promoted";
        const string password = "promoted user passphrase";
        var created = await api.CreateUserAsync(name, password);

        var before = await api.LoginForTokensAsync(name, password);
        using (var client = Bearer(before.AccessToken))
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/libraries")).StatusCode);

        using (var scope = api.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = (await users.FindByIdAsync(created.Id.ToString()))!;
            var result = await users.SetAdminAsync(user, true);
            Assert.True(result.Succeeded);
        }

        // The security stamp moved, so the old refresh token can't mint a token that
        // still says "not admin" (or, for a demotion, one that still says "admin").
        using (var anonymous = api.CreateClient())
        {
            var refresh = await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = before.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        }

        var after = await api.LoginForTokensAsync(name, password);
        using (var client = Bearer(after.AccessToken))
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/libraries")).StatusCode);
            var me = await client.GetFromJsonAsync<CurrentUser>("/api/auth/me", ApiFixture.Json);
            Assert.True(me!.IsAdmin);
        }
    }

    private HttpClient Bearer(string accessToken)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static HttpRequestMessage Request(string method, string route)
    {
        var request = new HttpRequestMessage(
            new HttpMethod(method),
            route.Replace("{library}", Guid.NewGuid().ToString()).Replace("{user}", Guid.NewGuid().ToString()));
        if (method is "POST" or "PATCH" or "PUT")
            request.Content = JsonContent.Create(new { });
        return request;
    }
}
