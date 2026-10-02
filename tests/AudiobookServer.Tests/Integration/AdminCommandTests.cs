using System.Net;
using System.Net.Http.Json;
using AudiobookServer.Api.Auth;
using AudiobookServer.Api.Cli;
using AudiobookServer.Api.Endpoints;
using AudiobookServer.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookServer.Tests.Integration;

/// <summary>
/// The `admin` command, run against the test host's services (what Program.cs hands it
/// in production), with the passphrase prompt answered from a queue.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class AdminCommandTests(ApiFixture api)
{
    private const string NewPassphrase = "chosen at the server";

    [Fact]
    public async Task Reset_password_prompts_and_the_new_passphrase_signs_in()
    {
        var name = Unique("reset");
        await api.CreateUserAsync(name, "the original passphrase");

        var run = await RunAsync(["reset-password", name], NewPassphrase, NewPassphrase);

        Assert.Equal(AdminCommand.Success, run.Code);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(name, "the original passphrase")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(name, NewPassphrase)).StatusCode);
    }

    [Fact]
    public async Task Mismatched_prompts_change_nothing()
    {
        var name = Unique("mismatch");

        var run = await RunAsync(["create-user", name], NewPassphrase, "something else entirely");

        Assert.Equal(AdminCommand.Failure, run.Code);
        await using var db = api.NewDbContext();
        Assert.False(await db.Users.AnyAsync(u => u.UserName == name));
    }

    [Fact]
    public async Task Create_user_admin_temporary_gets_the_claim_and_must_change()
    {
        var name = Unique("cli-admin");

        var run = await RunAsync(["create-user", name, "--admin", "--temporary"]);

        Assert.Equal(AdminCommand.Success, run.Code);
        var temporary = run.Output.Split('\n')
            .Single(l => l.StartsWith("Temporary passphrase: ", StringComparison.Ordinal))["Temporary passphrase: ".Length..].Trim();

        using var client = await api.CookieClientAsync(name, temporary);
        var me = await client.GetFromJsonAsync<CurrentUser>("/api/auth/me", ApiFixture.Json);
        Assert.True(me!.IsAdmin);
        Assert.True(me.MustChangePassword);

        // Even an admin does nothing else until the passphrase is their own.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/libraries")).StatusCode);
    }

    [Fact]
    public async Task Set_admin_never_removes_the_last_admin()
    {
        // Leave the seeded account as the only admin: other tests make admins too. The
        // collection runs its tests one at a time, so nothing else sees this window.
        List<Guid> demoted;
        using (var scope = api.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var others = await users.Users.Where(u => u.IsAdmin && u.UserName != ApiFixture.Username).ToListAsync();
            foreach (var user in others)
                await users.SetAdminAsync(user, false);
            demoted = others.Select(u => u.Id).ToList();
        }

        try
        {
            var run = await RunAsync(["set-admin", ApiFixture.Username, "false"]);
            Assert.Equal(AdminCommand.Failure, run.Code);
            Assert.Contains("only admin", run.Error);

            var name = Unique("second-admin");
            await api.CreateUserAsync(name, NewPassphrase);
            Assert.Equal(AdminCommand.Success, (await RunAsync(["set-admin", name, "true"])).Code);
            Assert.Equal(AdminCommand.Success, (await RunAsync(["set-admin", name, "false"])).Code);
        }
        finally
        {
            using var scope = api.Factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            foreach (var id in demoted)
                await users.SetAdminAsync((await users.FindByIdAsync(id.ToString()))!, true);
        }
    }

    [Fact]
    public async Task Enable_lets_a_disabled_account_back_in()
    {
        var name = Unique("disabled");
        await api.CreateUserAsync(name, NewPassphrase);
        using (var scope = api.Factory.Services.CreateScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountAdmin>();
            Assert.True((await accounts.DisableAsync((await accounts.FindAsync(name))!, actingUserId: null)).Succeeded);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await LoginAsync(name, NewPassphrase)).StatusCode);

        var users = await RunAsync(["users"]);
        Assert.Matches($"{name} .*disabled", users.Output);

        Assert.Equal(AdminCommand.Success, (await RunAsync(["enable", name])).Code);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(name, NewPassphrase)).StatusCode);
    }

    [Fact]
    public async Task Scan_by_name_runs_and_reports()
    {
        // An empty folder of its own: a scan of the fixture's libraries would remove the
        // books the other tests planted there, which have no files on disk.
        var root = Directory.CreateTempSubdirectory("audiobook-cli-scan").FullName;
        var name = Unique("Scan me");
        await using (var db = api.NewDbContext())
        {
            db.Libraries.Add(new Library { Id = Guid.NewGuid(), Name = name, RootPath = root, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var run = await RunAsync(["scan", name.ToUpperInvariant(), "--force"]);

        Assert.Equal(AdminCommand.Success, run.Code);
        Assert.Contains("Added 0", run.Output);
        Assert.Contains(name, (await RunAsync(["libraries"])).Output);
    }

    [Theory]
    [InlineData("no-such-verb")]
    [InlineData("users", "extra")]
    [InlineData("scan", "Test", "--bogus")]
    [InlineData("set-admin", "someone", "maybe")]
    public async Task Bad_usage_exits_2(params string[] args)
    {
        var run = await RunAsync(args);
        Assert.Equal(AdminCommand.Usage, run.Code);
        Assert.Contains("Usage:", run.Error);
    }

    [Fact]
    public async Task An_unknown_user_is_a_failure_not_a_crash()
    {
        var run = await RunAsync(["enable", Unique("nobody")]);
        Assert.Equal(AdminCommand.Failure, run.Code);
        Assert.Contains("No user", run.Error);
    }

    private sealed record Run(int Code, string Output, string Error);

    private async Task<Run> RunAsync(string[] args, params string[] answers)
    {
        var prompts = new Queue<string>(answers);
        var output = new StringWriter();
        var error = new StringWriter();
        var command = new AdminCommand(api.Factory.Services, output, error,
            _ => prompts.TryDequeue(out var answer) ? answer : null);

        var code = await command.RunAsync(args);
        return new Run(code, output.ToString(), error.ToString());
    }

    private async Task<HttpResponseMessage> LoginAsync(string username, string password)
    {
        using var client = api.CreateClient();
        return await client.PostAsJsonAsync("/api/auth/login", new { username, password });
    }

    private static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..8];
}
