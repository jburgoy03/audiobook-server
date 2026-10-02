using System.Globalization;
using System.Text;
using AudiobookServer.Api.Auth;
using AudiobookServer.Core.Data;
using AudiobookServer.Core.Scanning;
using Microsoft.EntityFrameworkCore;

namespace AudiobookServer.Api.Cli;

/// <summary>
/// Server jobs that don't belong in a browser, run as the API binary with `admin` first:
///
///   docker exec -it audiobook-api ./AudiobookServer.Api admin reset-password dean
///   dotnet run --project src/AudiobookServer.Api -- admin users
///
/// Program.cs builds the same host (configuration, database, Identity, scanner) and
/// hands it here instead of starting the web server. Whoever can run this already
/// controls the server, so it needs no sign-in: that's what makes it the recovery path
/// when the admin passphrase is lost or the admin account is disabled.
///
/// Account changes go through <see cref="AccountAdmin"/>, the same rules as the admin
/// page. Arguments are parsed by hand: seven verbs don't need a library.
///
/// Exit codes: 0 success, 1 failure (message on stderr), 2 bad usage.
/// </summary>
public sealed class AdminCommand(
    IServiceProvider services,
    TextWriter output,
    TextWriter error,
    Func<string, string?> readSecret)
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int Usage = 2;

    private const string UsageText = """
        Usage: AudiobookServer.Api admin <command>

          users                                   List accounts.
          create-user <name> [--admin] [--temporary]
                                                  Create an account. Prompts for its passphrase,
                                                  or generates one they must change (--temporary).
          reset-password <name> [--temporary]     Replace a passphrase (prompted, or generated with
                                                  --temporary). Clears a lockout; ends their sessions.
          set-admin <name> true|false             Grant or remove admin. Never removes the last admin.
          enable <name>                           Re-enable a disabled or locked-out account.
          libraries                               List libraries.
          scan <library> [--force]                Scan a library by name or ID. --force re-reads
                                                  every file. Ctrl+C cancels.

        Passphrases are prompted for, never taken as arguments, so they stay out of shell
        history. Prompting needs a terminal: docker exec -it.
        """;

    /// <summary>The console entry point: real stdin/stdout, and Ctrl+C cancels a scan.</summary>
    public static async Task<int> RunFromConsoleAsync(IServiceProvider services, string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // let the scan stop cleanly rather than kill the process
            cts.Cancel();
        };

        return await new AdminCommand(services, Console.Out, Console.Error, ReadSecretFromConsole)
            .RunAsync(args, cts.Token);
    }

    public async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        var flags = args.Where(a => a.StartsWith("--", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

        if (positional.Length == 0 || flags.Contains("--help"))
            return Fail(Usage, positional.Length == 0 ? "No command given." : null);

        string[] allowedFlags = positional[0] switch
        {
            "create-user" => ["--admin", "--temporary"],
            "reset-password" => ["--temporary"],
            "scan" => ["--force"],
            _ => [],
        };
        if (flags.FirstOrDefault(f => !allowedFlags.Contains(f)) is { } unknownFlag)
            return Fail(Usage, $"Unknown option {unknownFlag} for {positional[0]}.");

        try
        {
            return (positional[0], positional.Length) switch
            {
                ("users", 1) => await UsersAsync(ct),
                ("create-user", 2) => await CreateUserAsync(positional[1], flags.Contains("--admin"), flags.Contains("--temporary")),
                ("reset-password", 2) => await ResetPasswordAsync(positional[1], flags.Contains("--temporary")),
                ("set-admin", 3) => await SetAdminAsync(positional[1], positional[2]),
                ("enable", 2) => await EnableAsync(positional[1]),
                ("libraries", 1) => await LibrariesAsync(ct),
                ("scan", 2) => await ScanAsync(positional[1], flags.Contains("--force"), ct),
                ("users" or "create-user" or "reset-password" or "set-admin" or "enable" or "libraries" or "scan", _) =>
                    Fail(Usage, $"Wrong number of arguments for {positional[0]}."),
                _ => Fail(Usage, $"Unknown command '{positional[0]}'."),
            };
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("Cancelled.");
            return Failure;
        }
    }

    private async Task<int> UsersAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountAdmin>();

        var rows = (await accounts.ListAsync(ct)).Select(u => new[]
        {
            u.Username,
            u.IsAdmin ? "admin" : "",
            u.Disabled ? "disabled" : "",
            u.MustChangePassword ? "must change" : "",
            Date(u.CreatedAt),
            u.LastSeenAt is { } seen ? Date(seen) : "never",
        });
        WriteTable(["NAME", "ADMIN", "STATUS", "PASSPHRASE", "CREATED", "LAST LISTENED"], rows);
        return Success;
    }

    private async Task<int> CreateUserAsync(string username, bool isAdmin, bool temporary)
    {
        string? password = null;
        if (!temporary && (password = ReadNewPassphrase()) is null)
            return Failure;

        using var scope = services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountAdmin>();

        var result = await accounts.CreateAsync(username, password, isAdmin);
        if (!result.Succeeded)
            return Fail(Failure, result.Message);

        output.WriteLine($"Created {result.User!.UserName}{(isAdmin ? " (admin)" : "")}.");
        WriteTemporary(result.TemporaryPassword);
        return Success;
    }

    private async Task<int> ResetPasswordAsync(string username, bool temporary)
    {
        using var scope = services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountAdmin>();

        // Look the user up before prompting, so a typo in the name fails straight away.
        if (await accounts.FindAsync(username) is not { } user)
            return Fail(Failure, $"No user '{username}'.");

        string? password = null;
        if (!temporary && (password = ReadNewPassphrase()) is null)
            return Failure;

        var result = await accounts.ResetPasswordAsync(user, password);
        if (!result.Succeeded)
            return Fail(Failure, result.Message);

        output.WriteLine($"Passphrase for {user.UserName} replaced. Their other sessions have ended.");
        WriteTemporary(result.TemporaryPassword);
        if (AccountAdmin.IsDisabled(user))
            output.WriteLine($"The account is disabled. To let them in: admin enable {user.UserName}");
        return Success;
    }

    private async Task<int> SetAdminAsync(string username, string value)
    {
        if (!bool.TryParse(value, out var isAdmin))
            return Fail(Usage, $"Expected true or false, not '{value}'.");

        using var scope = services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountAdmin>();

        if (await accounts.FindAsync(username) is not { } user)
            return Fail(Failure, $"No user '{username}'.");

        var result = await accounts.SetAdminAsync(user, isAdmin);
        if (!result.Succeeded)
            return Fail(Failure, result.Message);

        output.WriteLine(isAdmin
            ? $"{user.UserName} is an admin. They need to sign out and in to see it."
            : $"{user.UserName} is no longer an admin.");
        return Success;
    }

    private async Task<int> EnableAsync(string username)
    {
        using var scope = services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountAdmin>();

        if (await accounts.FindAsync(username) is not { } user)
            return Fail(Failure, $"No user '{username}'.");

        var result = await accounts.EnableAsync(user);
        if (!result.Succeeded)
            return Fail(Failure, result.Message);

        output.WriteLine($"{user.UserName} is enabled and can sign in.");
        return Success;
    }

    private async Task<int> LibrariesAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AudiobookDbContext>();

        var libraries = await db.Libraries
            .AsNoTracking()
            .OrderBy(l => l.Name)
            .Select(l => new { l.Name, l.RootPath, l.IsPublic, Books = l.Books.Count, l.LastScanCompletedAt })
            .ToListAsync(ct);

        WriteTable(
            ["NAME", "VISIBILITY", "BOOKS", "LAST SCAN", "PATH"],
            libraries.Select(l => new[]
            {
                l.Name,
                l.IsPublic ? "public" : "private",
                l.Books.ToString(CultureInfo.InvariantCulture),
                l.LastScanCompletedAt is { } at ? Date(at) : "never",
                l.RootPath,
            }));
        return Success;
    }

    /// <summary>
    /// In-process, so no HTTP request for Cloudflare to cut off at 100 seconds: the
    /// reliable way to rescan a big library until background scanning exists.
    /// </summary>
    private async Task<int> ScanAsync(string nameOrId, bool force, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AudiobookDbContext>();

        var matches = Guid.TryParse(nameOrId, out var id)
            ? await db.Libraries.AsNoTracking().Where(l => l.Id == id).Select(l => new { l.Id, l.Name }).ToListAsync(ct)
            : await db.Libraries.AsNoTracking()
                .Where(l => l.Name.ToLower() == nameOrId.ToLower())
                .Select(l => new { l.Id, l.Name })
                .ToListAsync(ct);

        switch (matches.Count)
        {
            case 0:
                return Fail(Failure, $"No library '{nameOrId}'. See: admin libraries");
            case > 1:
                return Fail(Failure, $"More than one library is called '{nameOrId}'. Use its ID.");
        }

        var library = matches[0];
        output.WriteLine($"Scanning {library.Name}{(force ? " (force: re-reading every file)" : "")}...");

        var scanner = scope.ServiceProvider.GetRequiredService<ILibraryScanService>();
        var report = await scanner.ScanAsync(library.Id, force, ct);

        output.WriteLine(
            $"Added {report.BooksAdded}, updated {report.BooksUpdated}, unchanged {report.BooksUnchanged}, " +
            $"removed {report.BooksRemoved}, failures {report.Failures}, " +
            $"mp3 durations corrected {report.FilesDurationCorrected}.");
        return report.Failures > 0 ? Failure : Success;
    }

    /// <summary>Prompts twice. Null (with the reason on stderr) when empty or mismatched.</summary>
    private string? ReadNewPassphrase()
    {
        var first = readSecret("New passphrase: ");
        if (string.IsNullOrEmpty(first))
        {
            error.WriteLine("No passphrase entered. Prompting needs a terminal: run docker exec with -it.");
            return null;
        }

        var second = readSecret("Again: ");
        if (first != second)
        {
            error.WriteLine("The two passphrases don't match. Nothing changed.");
            return null;
        }

        return first;
    }

    private void WriteTemporary(string? temporaryPassword)
    {
        if (temporaryPassword is null)
            return;
        output.WriteLine($"Temporary passphrase: {temporaryPassword}");
        output.WriteLine("They'll choose their own when they sign in. It won't be shown again.");
    }

    private int Fail(int code, string? message)
    {
        if (message is not null)
            error.WriteLine(message);
        if (code == Usage)
            error.WriteLine(UsageText);
        return code;
    }

    private void WriteTable(string[] header, IEnumerable<string[]> rows)
    {
        var all = rows.Prepend(header).ToList();
        var widths = header.Select((_, i) => all.Max(r => r[i].Length)).ToArray();
        foreach (var row in all)
            output.WriteLine(string.Join("  ", row.Select((cell, i) => cell.PadRight(widths[i]))).TrimEnd());
    }

    private static string Date(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads without echoing. The prompt goes to stderr so stdout carries only results.
    /// With stdin redirected (no -t), it falls back to a plain line, which is empty when
    /// docker exec was run without -i: ReadNewPassphrase then says to use -it.
    /// </summary>
    private static string? ReadSecretFromConsole(string prompt)
    {
        Console.Error.Write(prompt);
        if (Console.IsInputRedirected)
            return Console.ReadLine();

        var text = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (text.Length > 0)
                    text.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar))
                text.Append(key.KeyChar);
        }
        Console.Error.WriteLine();
        return text.ToString();
    }
}
