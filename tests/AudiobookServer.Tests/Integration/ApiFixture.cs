using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AudiobookServer.Core.Data;
using AudiobookServer.Core.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace AudiobookServer.Tests.Integration;

/// <summary>
/// One Postgres container and one running API for every integration test: starting
/// them costs seconds, the tests milliseconds. Tests that write create their own books
/// (<see cref="CreateBookAsync"/>), so sharing the database doesn't couple them.
///
/// Needs Docker. Skip these with: dotnet test --filter Category!=Integration
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    /// <summary>The seeded account. The seeder makes it an admin.</summary>
    public const string Username = "listener";
    public const string Password = "correct horse battery staple";

    /// <summary>A second account without admin, created after startup like any later user.</summary>
    public const string VisitorUsername = "visitor";
    public const string VisitorPassword = "a visitor's own passphrase";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    // Same image as docker-compose.yml, so the tests run against the Postgres the app uses.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "audiobook-tests-" + Guid.NewGuid().ToString("N"));

    private Guid _libraryId;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    /// <summary>A book with one real file on disk, for the stream and cover endpoints.</summary>
    public Guid StreamableBookId { get; private set; }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var libraryRoot = Path.Combine(_root, "library");
        Directory.CreateDirectory(Path.Combine(libraryRoot, "Book"));
        await File.WriteAllBytesAsync(Path.Combine(libraryRoot, "Book", "01.mp3"), new byte[4096]);

        await using (var db = NewDbContext())
        {
            await db.Database.MigrateAsync();

            var library = new Library
            {
                Id = Guid.NewGuid(),
                Name = "Test",
                RootPath = libraryRoot,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Libraries.Add(library);
            await db.SaveChangesAsync();
            _libraryId = library.Id;
        }

        StreamableBookId = await CreateBookAsync(3600, relativeFilePath: "Book/01.mp3");

        // Environment variables rather than WebApplicationFactory settings: Program.cs
        // reads some configuration (key and cover directories) before Build(), and
        // environment variables are guaranteed to be in place by then. They also beat
        // user-secrets, so the developer's own database is never touched.
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Auth__SeedUser__Username", Username);
        Environment.SetEnvironmentVariable("Auth__SeedUser__Password", Password);
        Environment.SetEnvironmentVariable("DataProtection__KeysDirectory", Path.Combine(_root, "keys"));
        Environment.SetEnvironmentVariable("Covers__Directory", Path.Combine(_root, "covers"));

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseEnvironment("Testing"));

        // Starts the host, which runs the user seeder.
        _ = Factory.Server;

        await CreateUserAsync(VisitorUsername, VisitorPassword);
    }

    /// <summary>Creates a user (not an admin) through Identity, as the app would.</summary>
    public async Task<User> CreateUserAsync(string username, string password)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User { Id = Guid.NewGuid(), UserName = username, CreatedAt = DateTimeOffset.UtcNow };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
        return user;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    public AudiobookDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AudiobookDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    /// <summary>Inserts a book with one file, without scanning. Returns its ID.</summary>
    public async Task<Guid> CreateBookAsync(double durationSeconds, string relativeFilePath = "missing.mp3")
    {
        await using var db = NewDbContext();
        var id = Guid.NewGuid();
        db.Books.Add(new Book
        {
            Id = id,
            LibraryId = _libraryId,
            Title = "Book " + id.ToString("N")[..6],
            RelativePath = id.ToString("N"),
            DurationSeconds = durationSeconds,
            AddedAt = DateTimeOffset.UtcNow,
            Files =
            [
                new AudioFile
                {
                    Id = Guid.NewGuid(),
                    RelativePath = relativeFilePath,
                    Sequence = 0,
                    StartOffsetSeconds = 0,
                    DurationSeconds = durationSeconds,
                    HeaderDurationSeconds = durationSeconds,
                    MimeType = "audio/mpeg",
                    SizeBytes = 4096,
                },
            ],
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>A client with a cookie jar and no redirect following, as a browser's fetch would see it.</summary>
    public HttpClient CreateClient() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    /// <summary>Signed in with a cookie. Defaults to the seeded admin.</summary>
    public async Task<HttpClient> CookieClientAsync(string username = Username, string password = Password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login?useCookies=true", new { username, password });
        response.EnsureSuccessStatusCode();
        return client;
    }

    public async Task<TokenResponse> LoginForTokensAsync(string username = Username, string password = Password)
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Json))!;
    }

    /// <summary>Signed in with a bearer token. Defaults to the seeded admin.</summary>
    public async Task<HttpClient> BearerClientAsync(string username = Username, string password = Password)
    {
        var tokens = await LoginForTokensAsync(username, password);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }
}

public sealed record TokenResponse(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
