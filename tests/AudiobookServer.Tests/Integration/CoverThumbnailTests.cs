using System.Diagnostics;
using System.Net;
using AudiobookServer.Core.Media;

namespace AudiobookServer.Tests.Integration;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class CoverThumbnailTests(ApiFixture api)
{
    [Fact]
    public async Task Only_the_published_sizes_are_accepted()
    {
        using var client = await api.CookieClientAsync(ApiFixture.VisitorUsername, ApiFixture.VisitorPassword);
        var response = await client.GetAsync($"/api/books/{api.PublicBookId}/cover?size=123");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_cover_that_cant_be_thumbnailed_is_served_as_it_is()
    {
        // The fixture's stand-in cover is four bytes: no image ffmpeg could scale (and
        // ffmpeg may not be installed at all). The original still comes back.
        using var client = await api.CookieClientAsync(ApiFixture.VisitorUsername, ApiFixture.VisitorPassword);
        var response = await client.GetAsync($"/api/books/{api.PublicBookId}/cover?size=640");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([0xFF, 0xD8, 0xFF, 0xD9], await response.Content.ReadAsByteArrayAsync());
    }

    [SkippableTheory]
    [InlineData(320, 320, 240)]
    [InlineData(640, 640, 480)]
    [InlineData(1080, 1080, 810)]
    public async Task A_large_cover_is_scaled_to_the_requested_longest_side(int size, int width, int height)
    {
        var bookId = await BookWithCoverAsync(2000, 1500);
        using var client = await api.CookieClientAsync();

        var response = await client.GetAsync($"/api/books/{bookId}/cover?size={size}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        var image = ImageHeader.TryRead(await response.Content.ReadAsStreamAsync());
        Assert.Equal(new ImageInfo("jpeg", width, height), image);
    }

    [SkippableFact]
    public async Task A_small_cover_is_never_enlarged()
    {
        var bookId = await BookWithCoverAsync(300, 300);
        using var client = await api.CookieClientAsync();

        var response = await client.GetAsync($"/api/books/{bookId}/cover?size=1080");

        var image = ImageHeader.TryRead(await response.Content.ReadAsStreamAsync());
        Assert.Equal(300, image?.Width);
    }

    [SkippableFact]
    public async Task A_thumbnail_is_made_once_and_revalidates_like_the_original()
    {
        var bookId = await BookWithCoverAsync(2000, 1500);
        using var client = await api.CookieClientAsync();

        var first = await client.GetAsync($"/api/books/{bookId}/cover?size=640");
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);

        using var again = new HttpRequestMessage(HttpMethod.Get, $"/api/books/{bookId}/cover?size=640");
        again.Headers.IfNoneMatch.Add(etag);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(again)).StatusCode);
    }

    /// <summary>A book whose stored cover is a real jpeg of the given size, drawn by ffmpeg.</summary>
    private async Task<Guid> BookWithCoverAsync(int width, int height)
    {
        Skip.IfNot(FfmpegAvailable.Value, "ffmpeg isn't on the PATH.");

        var bookId = await api.CreateBookAsync(60, withCover: true);
        var path = api.StoredCoverPath(bookId);
        File.Delete(path);

        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            ["-v", "error", "-y", "-f", "lavfi", "-i", $"color=c=purple:s={width}x{height}", "-frames:v", "1", "-f", "image2", path])
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        await ffmpeg.WaitForExitAsync();
        Assert.True(File.Exists(path), await ffmpeg.StandardError.ReadToEndAsync());
        return bookId;
    }

    private static readonly Lazy<bool> FfmpegAvailable = new(() =>
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("ffmpeg", ["-version"])
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            });
            process!.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    });
}
