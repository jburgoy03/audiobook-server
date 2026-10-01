using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookServer.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookServer.Tests.Integration;

/// <summary>
/// The regression that would leak the private library. Every book endpoint, as each
/// kind of user, against a book in each kind of library:
///
///   {admin, user} × {public, private} × {list, detail, cover, stream GET, stream HEAD,
///                                        progress GET, progress list, progress POST}
///
/// The rule: an admin sees everything, a user sees public libraries only, and a book
/// you can't see is a 404 (or simply absent from a list), never a 403.
///
/// A new book endpoint belongs in this matrix. If it isn't here, nothing checks that
/// it applies VisibleBooks.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class VisibilityTests(ApiFixture api)
{
    public static readonly string[] Endpoints =
    [
        "list", "detail", "cover", "stream GET", "stream HEAD", "progress GET", "progress list", "progress POST",
    ];

    public static TheoryData<string, string, string> Matrix
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var user in new[] { "admin", "user" })
                foreach (var library in new[] { "public", "private" })
                    foreach (var endpoint in Endpoints)
                        data.Add(user, library, endpoint);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Admins_see_everything_users_see_public_only(string user, string library, string endpoint)
    {
        var (username, password) = user == "admin"
            ? (ApiFixture.Username, ApiFixture.Password)
            : (ApiFixture.VisitorUsername, ApiFixture.VisitorPassword);
        var bookId = library == "public" ? api.PublicBookId : api.PrivateBookId;
        var visible = user == "admin" || library == "public";

        using var client = await api.CookieClientAsync(username, password);

        switch (endpoint)
        {
            case "list":
            {
                var books = await client.GetFromJsonAsync<JsonElement[]>("/api/books", ApiFixture.Json);
                var ids = books!.Select(b => b.GetProperty("id").GetGuid());
                Assert.Equal(visible, ids.Contains(bookId));
                break;
            }

            case "progress list":
            {
                // Planted directly, as if the book had been visible when the row was
                // written and its library became private afterwards.
                await PlantProgressAsync(await api.UserIdAsync(username), bookId);
                var rows = await client.GetFromJsonAsync<JsonElement[]>("/api/progress", ApiFixture.Json);
                var ids = rows!.Select(r => r.GetProperty("bookId").GetGuid());
                Assert.Equal(visible, ids.Contains(bookId));
                break;
            }

            default:
            {
                using var response = await client.SendAsync(Request(endpoint, bookId));
                if (visible)
                {
                    Assert.True(
                        response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent,
                        $"{endpoint} as {user} on a {library} book: expected success, got {(int)response.StatusCode}");
                }
                else
                {
                    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                }
                break;
            }
        }
    }

    [Fact]
    public void The_matrix_covers_every_book_route()
    {
        // A cheap tripwire: if a route under /api/books is added without a row above,
        // the count of mapped book routes changes and this fails.
        var sources = api.Factory.Services
            .GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>()
            .SelectMany(s => s.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? "")
            .Where(p => p.StartsWith("/api/books") || p.StartsWith("/api/progress"))
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        Assert.Equal(
            [
                "/api/books",
                "/api/books/{bookId:guid}/files/{sequence:int}/stream",
                "/api/books/{bookId:guid}/progress",
                "/api/books/{id:guid}",
                "/api/books/{id:guid}/cover",
                "/api/progress",
            ],
            sources);
    }

    [Fact]
    public async Task A_public_book_carries_its_library_credit()
    {
        using var client = await api.CookieClientAsync(ApiFixture.VisitorUsername, ApiFixture.VisitorPassword);
        var book = await client.GetFromJsonAsync<JsonElement>($"/api/books/{api.PublicBookId}", ApiFixture.Json);
        Assert.Equal("Public domain · Test", book.GetProperty("credit").GetString());
    }

    private static HttpRequestMessage Request(string endpoint, Guid bookId) => endpoint switch
    {
        "detail" => new(HttpMethod.Get, $"/api/books/{bookId}"),
        "cover" => new(HttpMethod.Get, $"/api/books/{bookId}/cover"),
        "stream GET" => new(HttpMethod.Get, $"/api/books/{bookId}/files/0/stream"),
        "stream HEAD" => new(HttpMethod.Head, $"/api/books/{bookId}/files/0/stream"),
        "progress GET" => new(HttpMethod.Get, $"/api/books/{bookId}/progress"),
        "progress POST" => new(HttpMethod.Post, "/api/progress")
        {
            Content = JsonContent.Create(new
            {
                bookId,
                positionSeconds = 1.0,
                reportedAt = DateTimeOffset.UtcNow,
                isFinished = false,
                @override = false,
            }),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, null),
    };

    private async Task PlantProgressAsync(Guid userId, Guid bookId)
    {
        await using var db = api.NewDbContext();
        if (await db.PlaybackPositions.AnyAsync(p => p.UserId == userId && p.BookId == bookId))
            return;

        db.PlaybackPositions.Add(new PlaybackPosition
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BookId = bookId,
            PositionSeconds = 1,
            ReportedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
