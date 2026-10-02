using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AudiobookServer.Api.Endpoints;
using AudiobookServer.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookServer.Tests.Integration;

/// <summary>
/// Auth regressions are silent: an endpoint left anonymous by accident looks exactly
/// like one that works. These pin down who can reach what, and how.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class AuthTests(ApiFixture api)
{
    public static TheoryData<string, string> ProtectedRoutes => new()
    {
        { "GET", "/api/libraries" },
        { "POST", "/api/libraries" },
        { "PATCH", "/api/libraries/{library}" },
        { "POST", "/api/libraries/{library}/scan" },
        { "GET", "/api/books" },
        { "GET", "/api/books/{book}" },
        { "GET", "/api/books/{book}/cover" },
        { "GET", "/api/books/{book}/files/0/stream" },
        { "HEAD", "/api/books/{book}/files/0/stream" },
        { "GET", "/api/books/{book}/progress" },
        { "GET", "/api/progress" },
        { "POST", "/api/progress" },
        { "GET", "/api/auth/me" },
        { "POST", "/api/auth/change-password" },
        { "GET", "/api/admin/users" },
        { "POST", "/api/admin/users" },
        { "POST", "/api/admin/users/{user}/reset-password" },
        { "POST", "/api/admin/users/{user}/disable" },
        { "POST", "/api/admin/users/{user}/enable" },
        { "GET", "/api/no-such-route" },
    };

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task Anonymous_requests_get_401_not_a_redirect(string method, string route)
    {
        using var client = api.CreateClient();
        var url = route
            .Replace("{book}", api.StreamableBookId.ToString())
            .Replace("{library}", Guid.NewGuid().ToString())
            .Replace("{user}", Guid.NewGuid().ToString());

        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
            request.Content = JsonContent.Create(new { });

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_is_401()
    {
        using var client = api.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login?useCookies=true", new { username = ApiFixture.Username, password = "not the password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Cookie_login_sets_a_strict_http_only_cookie()
    {
        using var client = api.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login?useCookies=true", new { username = ApiFixture.Username, password = ApiFixture.Password });

        response.EnsureSuccessStatusCode();
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("audiobook.auth="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_cookie_reaches_the_media_endpoints()
    {
        // The reason for cookie auth: <audio> and <img> send cookies but not headers.
        using var client = await api.CookieClientAsync();

        var me = await client.GetFromJsonAsync<CurrentUser>("/api/auth/me");
        Assert.Equal(ApiFixture.Username, me!.Username);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/books/{api.StreamableBookId}/files/0/stream");
        request.Headers.Range = new RangeHeaderValue(0, 99);
        using var stream = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, stream.StatusCode);
        Assert.Equal(100, (await stream.Content.ReadAsByteArrayAsync()).Length);

        // No cover was extracted for this book: authenticated, so a 404 rather than a 401.
        using var cover = await client.GetAsync($"/api/books/{api.StreamableBookId}/cover");
        Assert.Equal(HttpStatusCode.NotFound, cover.StatusCode);
    }

    [Fact]
    public async Task A_bearer_token_reaches_the_media_endpoints()
    {
        using var client = await api.BearerClientAsync();

        using var stream = await client.GetAsync($"/api/books/{api.StreamableBookId}/files/0/stream");
        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
    }

    [Fact]
    public async Task A_refresh_token_issues_a_working_access_token()
    {
        var tokens = await api.LoginForTokensAsync();

        using var anonymous = api.CreateClient();
        var refreshed = await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        refreshed.EnsureSuccessStatusCode();
        var next = (await refreshed.Content.ReadFromJsonAsync<TokenResponse>(ApiFixture.Json))!;

        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", next.AccessToken);
        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task A_forged_refresh_token_is_401()
    {
        using var client = api.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "not-a-token" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_access_token_is_not_accepted_as_a_refresh_token()
    {
        var tokens = await api.LoginForTokensAsync();
        using var client = api.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.AccessToken });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_cookie_session()
    {
        using var client = await api.CookieClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_api_route_is_404_when_signed_in()
    {
        using var client = await api.CookieClientAsync();
        using var response = await client.GetAsync("/api/no-such-route");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Five_failures_lock_the_account()
    {
        // Its own user, so the lockout can't affect the other tests.
        const string name = "lockout-test";
        const string password = "the right passphrase";
        using (var scope = api.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var created = await users.CreateAsync(
                new User { Id = Guid.NewGuid(), UserName = name, CreatedAt = DateTimeOffset.UtcNow }, password);
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        using var client = api.CreateClient();
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/auth/login", new { username = name, password = "wrong guess" });

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = name, password });
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }
}
