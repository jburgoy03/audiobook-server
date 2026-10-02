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

    /// <summary>A non-admin with a grant on the private library. For the visibility matrix.</summary>
    public const string GrantedUsername = "granted";
    public const string GrantedPassword = "a granted listener's passphrase";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    // Same image as docker-compose.yml, so the tests run against the Postgres the app uses.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "audiobook-tests-" + Guid.NewGuid().ToString("N"));

    private string CoversDirectory => Path.Combine(_root, "covers");

    /// <summary>The fixture's original library. Private, as every library is by default.</summary>
    public Guid PrivateLibraryId { get; private set; }

    public Guid PublicLibraryId { get; private set; }

    /// <summary>A streamable book with a cover, in the private library. For the visibility matrix.</summary>
    public Guid PrivateBookId { get; private set; }

    /// <summary>A streamable book with a cover, in the public library. For the visibility matrix.</summary>
    public Guid PublicBookId { get; private set; }

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    /// <summary>A book with one real file on disk, for the stream and cover endpoints.</summary>
    public Guid StreamableBookId { get; private set; }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var libraryRoot = Path.Combine(_root, "library");
        var publicRoot = Path.Combine(_root, "public");
        foreach (var root in new[] { libraryRoot, publicRoot })
        {
            Directory.CreateDirectory(Path.Combine(root, "Book"));
            await File.WriteAllBytesAsync(Path.Combine(root, "Book", "01.mp3"), new byte[4096]);
        }

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
            var publicLibrary = new Library
            {
                Id = Guid.NewGuid(),
                Name = "Public",
                RootPath = publicRoot,
                IsPublic = true,
                Credit = "Public domain · Test",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Libraries.AddRange(library, publicLibrary);
            await db.SaveChangesAsync();
            PrivateLibraryId = library.Id;
            PublicLibraryId = publicLibrary.Id;
        }

        StreamableBookId = await CreateBookAsync(3600, relativeFilePath: "Book/01.mp3");
        PrivateBookId = await CreateBookAsync(3600, "Book/01.mp3", PrivateLibraryId, withCover: true);
        PublicBookId = await CreateBookAsync(3600, "Book/01.mp3", PublicLibraryId, withCover: true);

        // Environment variables rather than WebApplicationFactory settings: Program.cs
        // reads some configuration (key and cover directories) before Build(), and
        // environment variables are guaranteed to be in place by then. They also beat
        // user-secrets, so the developer's own database is never touched.
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Auth__SeedUser__Username", Username);
        Environment.SetEnvironmentVariable("Auth__SeedUser__Password", Password);
        Environment.SetEnvironmentVariable("DataProtection__KeysDirectory", Path.Combine(_root, "keys"));
        Environment.SetEnvironmentVariable("Covers__Directory", CoversDirectory);

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseEnvironment("Testing"));

        // Starts the host, which runs the user seeder.
        _ = Factory.Server;

        await CreateUserAsync(VisitorUsername, VisitorPassword);

        var granted = await CreateUserAsync(GrantedUsername, GrantedPassword);
        await using (var db = NewDbContext())
        {
            db.LibraryGrants.Add(new LibraryGrant
            {
                UserId = granted.Id, LibraryId = PrivateLibraryId, GrantedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
    }

    public async Task<Guid> UserIdAsync(string username)
    {
        await using var db = NewDbContext();
        return await db.Users.Where(u => u.UserName == username).Select(u => u.Id).SingleAsync();
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

    /// <summary>
    /// Inserts a book with one file, without scanning. Returns its ID. Goes in the
    /// private library unless told otherwise. withCover writes a stand-in image to the
    /// cover directory, so the cover endpoint has something to serve.
    /// </summary>
    public async Task<Guid> CreateBookAsync(
        double durationSeconds,
        string relativeFilePath = "missing.mp3",
        Guid? libraryId = null,
        bool withCover = false)
    {
        await using var db = NewDbContext();
        var id = Guid.NewGuid();

        string? coverPath = null;
        if (withCover)
        {
            Directory.CreateDirectory(CoversDirectory);
            coverPath = $"{id}.jpg";
            await File.WriteAllBytesAsync(Path.Combine(CoversDirectory, coverPath), [0xFF, 0xD8, 0xFF, 0xD9]);
        }

        db.Books.Add(new Book
        {
            Id = id,
            LibraryId = libraryId ?? PrivateLibraryId,
            CoverPath = coverPath,
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
