using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookServer.Api.Auth;
using AudiobookServer.Api.Endpoints;

namespace AudiobookServer.Tests.Integration;

/// <summary>
/// Accounts made by the admin: the temporary passphrase, the must-change gate the
/// server enforces, disabling, and resets. Each test makes its own users, so they
/// can't disturb the fixture's accounts.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class AdminUserTests(ApiFixture api)
{
    private const string ChosenPassphrase = "a passphrase of my own";

    [Fact]
    public async Task A_new_user_must_choose_a_passphrase_and_then_sees_only_public_books()
    {
        var created = await CreateAsync();
        Assert.Matches("^[a-z2-9]{4}(-[a-z2-9]{4}){4}$", created.TemporaryPassword);

        using var client = await api.CookieClientAsync(created.Username, created.TemporaryPassword);

        var me = await client.GetFromJsonAsync<CurrentUser>("/api/auth/me", ApiFixture.Json);
        Assert.True(me!.MustChangePassword);
        Assert.False(me.IsAdmin);

        // Enforced by the server, not just routed around by the client.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/books")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/progress")).StatusCode);

        var change = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = created.TemporaryPassword, newPassword = ChosenPassphrase });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        me = await client.GetFromJsonAsync<CurrentUser>("/api/auth/me", ApiFixture.Json);
        Assert.False(me!.MustChangePassword);

        var books = await client.GetFromJsonAsync<JsonElement[]>("/api/books", ApiFixture.Json);
        var ids = books!.Select(b => b.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(api.PublicBookId, ids);
        Assert.DoesNotContain(api.PrivateBookId, ids);

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(created.Username, created.TemporaryPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(created.Username, ChosenPassphrase)).StatusCode);
    }

    [Fact]
    public async Task Changing_the_passphrase_keeps_this_session_and_ends_the_others()
    {
        var created = await CreateAsync();
        var otherSession = await api.LoginForTokensAsync(created.Username, created.TemporaryPassword);
        using var client = await api.CookieClientAsync(created.Username, created.TemporaryPassword);

        var change = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = created.TemporaryPassword, newPassword = ChosenPassphrase });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        // The re-issued cookie works, and carries the new claims.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/books")).StatusCode);

        using var anonymous = api.CreateClient();
        var refresh = await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = otherSession.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task A_short_or_unchanged_or_wrong_passphrase_is_400()
    {
        var created = await CreateAsync();
        using var client = await api.CookieClientAsync(created.Username, created.TemporaryPassword);

        foreach (var (current, next) in new[]
        {
            (created.TemporaryPassword, "too short"),
            (created.TemporaryPassword, created.TemporaryPassword),
            ("not the current one", ChosenPassphrase),
        })
        {
            var response = await client.PostAsJsonAsync("/api/auth/change-password",
                new { currentPassword = current, newPassword = next });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var me = await client.GetFromJsonAsync<CurrentUser>("/api/auth/me", ApiFixture.Json);
        Assert.True(me!.MustChangePassword);
    }

    [Fact]
    public async Task A_taken_name_is_409()
    {
        using var admin = await api.CookieClientAsync();
        var response = await admin.PostAsJsonAsync("/api/admin/users", new { username = ApiFixture.VisitorUsername });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_list_shows_each_account_once()
    {
        using var admin = await api.CookieClientAsync();
        var users = await admin.GetFromJsonAsync<AccountSummary[]>("/api/admin/users", ApiFixture.Json);

        var visitor = Assert.Single(users!, u => u.Username == ApiFixture.VisitorUsername);
        Assert.False(visitor.IsAdmin);
        Assert.False(visitor.Disabled);
        Assert.True(Assert.Single(users!, u => u.Username == ApiFixture.Username).IsAdmin);
    }

    [Fact]
    public async Task Disabling_ends_sessions_and_says_disabled_only_to_the_right_passphrase()
    {
        var (name, password) = await SettledUserAsync();
        var tokens = await api.LoginForTokensAsync(name, password);
        using var admin = await api.CookieClientAsync();
        var id = await api.UserIdAsync(name);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/users/{id}/disable", null)).StatusCode);

        using (var anonymous = api.CreateClient())
        {
            var refresh = await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        }

        var right = await LoginAsync(name, password);
        Assert.Equal(HttpStatusCode.Forbidden, right.StatusCode);
        Assert.Contains("disabled", await right.Content.ReadAsStringAsync());

        // A guesser sees an ordinary lockout, not "disabled".
        var wrong = await LoginAsync(name, "a wrong guess entirely");
        Assert.Equal(HttpStatusCode.TooManyRequests, wrong.StatusCode);
        Assert.DoesNotContain("disabled", await wrong.Content.ReadAsStringAsync());

        var users = await admin.GetFromJsonAsync<AccountSummary[]>("/api/admin/users", ApiFixture.Json);
        Assert.True(Assert.Single(users!, u => u.Id == id).Disabled);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/users/{id}/enable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(name, password)).StatusCode);
    }

    [Fact]
    public async Task The_admin_cannot_disable_themselves()
    {
        using var admin = await api.CookieClientAsync();
        var self = await api.UserIdAsync(ApiFixture.Username);

        var response = await admin.PostAsync($"/api/admin/users/{self}/disable", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(ApiFixture.Username, ApiFixture.Password)).StatusCode);
    }

    [Fact]
    public async Task A_reset_replaces_the_passphrase_and_requires_a_new_choice()
    {
        var (name, password) = await SettledUserAsync();
        using var admin = await api.CookieClientAsync();
        var id = await api.UserIdAsync(name);

        var response = await admin.PostAsync($"/api/admin/users/{id}/reset-password", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reset = (await response.Content.ReadFromJsonAsync<TemporaryPasswordResponse>(ApiFixture.Json))!;

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(name, password)).StatusCode);

        using var client = await api.CookieClientAsync(name, reset.TemporaryPassword);
        var me = await client.GetFromJsonAsync<CurrentUser>("/api/auth/me", ApiFixture.Json);
        Assert.True(me!.MustChangePassword);
    }

    [Fact]
    public async Task Unknown_users_are_404()
    {
        using var admin = await api.CookieClientAsync();
        foreach (var action in new[] { "reset-password", "disable", "enable" })
        {
            var response = await admin.PostAsync($"/api/admin/users/{Guid.NewGuid()}/{action}", null);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private async Task<TemporaryPasswordResponse> CreateAsync()
    {
        using var admin = await api.CookieClientAsync();
        var name = "listener-" + Guid.NewGuid().ToString("N")[..8];
        var response = await admin.PostAsJsonAsync("/api/admin/users", new { username = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TemporaryPasswordResponse>(ApiFixture.Json))!;
    }

    /// <summary>A user with a passphrase of their own, made directly (no must-change).</summary>
    private async Task<(string Name, string Password)> SettledUserAsync()
    {
        var name = "settled-" + Guid.NewGuid().ToString("N")[..8];
        await api.CreateUserAsync(name, ChosenPassphrase);
        return (name, ChosenPassphrase);
    }

    private async Task<HttpResponseMessage> LoginAsync(string username, string password)
    {
        using var client = api.CreateClient();
        return await client.PostAsJsonAsync("/api/auth/login", new { username, password });
    }
}
