using System.Net;
using System.Net.Http.Json;
using AudiobookServer.Api.Endpoints;
using AudiobookServer.Core.Progress;

namespace AudiobookServer.Tests.Integration;

/// <summary>
/// The endpoint around ProgressRules: clamping, device registration, the response
/// shape, and that a rejected report leaves the stored row untouched. The rule's
/// individual cases are unit-tested in ProgressRulesTests.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class ProgressTests(ApiFixture api)
{
    private static readonly Guid Laptop = Guid.NewGuid();
    private static readonly Guid Phone = Guid.NewGuid();

    private static ProgressReport Report(
        Guid book, double position, Guid? device, DateTimeOffset? at = null,
        bool finished = false, bool isOverride = false, string? name = null) =>
        new(book, position, at ?? DateTimeOffset.UtcNow, device, name, finished, isOverride);

    private static async Task<ProgressResult> PostAsync(HttpClient client, ProgressReport report)
    {
        var response = await client.PostAsJsonAsync("/api/progress", report, ApiFixture.Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProgressResult>(ApiFixture.Json))!;
    }

    [Fact]
    public async Task A_report_is_stored_and_listed()
    {
        using var client = await api.BearerClientAsync();
        var book = await api.CreateBookAsync(3600);

        var result = await PostAsync(client, Report(book, 120, Laptop, name: "Firefox on Windows"));

        Assert.True(result.Accepted);
        Assert.Equal(ProgressReason.First, result.Reason);
        Assert.Equal(120, result.Progress.PositionSeconds);
        Assert.Equal("Firefox on Windows", result.Progress.DeviceName);

        var all = await client.GetFromJsonAsync<List<ProgressDto>>("/api/progress", ApiFixture.Json);
        var listed = Assert.Single(all!, p => p.BookId == book);
        Assert.Equal(120, listed.PositionSeconds);
        Assert.Equal(Laptop, listed.DeviceId);

        var one = await client.GetFromJsonAsync<ProgressDto>($"/api/books/{book}/progress", ApiFixture.Json);
        Assert.Equal(120, one!.PositionSeconds);
    }

    [Fact]
    public async Task A_book_without_progress_returns_204()
    {
        using var client = await api.BearerClientAsync();
        var book = await api.CreateBookAsync(3600);

        using var response = await client.GetAsync($"/api/books/{book}/progress");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_rejected_report_returns_what_the_server_kept()
    {
        using var client = await api.BearerClientAsync();
        var book = await api.CreateBookAsync(3600);
        await PostAsync(client, Report(book, 2000, Phone, name: "Pixel"));

        var result = await PostAsync(client, Report(book, 500, Laptop));

        Assert.False(result.Accepted);
        Assert.Equal(ProgressReason.Behind, result.Reason);
        Assert.Equal(2000, result.Progress.PositionSeconds);
        Assert.Equal("Pixel", result.Progress.DeviceName);
    }

    [Fact]
    public async Task Override_and_same_device_rewinds_are_stored()
    {
        using var client = await api.BearerClientAsync();
        var book = await api.CreateBookAsync(3600);
        var t = DateTimeOffset.UtcNow.AddMinutes(-10);

        await PostAsync(client, Report(book, 2000, Phone, t));
        var rewind = await PostAsync(client, Report(book, 1500, Phone, t.AddMinutes(1)));
        Assert.Equal(ProgressReason.SameDevice, rewind.Reason);

        var forced = await PostAsync(client, Report(book, 100, Laptop, t.AddMinutes(2), isOverride: true));
        Assert.Equal(ProgressReason.Override, forced.Reason);
        Assert.Equal(100, forced.Progress.PositionSeconds);
    }

    [Fact]
    public async Task Positions_are_clamped_to_the_book()
    {
        using var client = await api.BearerClientAsync();
        var book = await api.CreateBookAsync(3600);

        var past = await PostAsync(client, Report(book, 99_999, Laptop));
        Assert.Equal(3600, past.Progress.PositionSeconds);

        var negative = await PostAsync(client, Report(book, -50, Laptop, isOverride: true));
        Assert.Equal(0, negative.Progress.PositionSeconds);
    }

    [Fact]
    public async Task A_future_timestamp_is_clamped_to_server_time()
    {
        using var client = await api.BearerClientAsync();
        var book = await api.CreateBookAsync(3600);

        var result = await PostAsync(client, Report(book, 10, Laptop, DateTimeOffset.UtcNow.AddDays(1)));

        Assert.True(result.Progress.ReportedAt <= DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task Finished_is_kept_against_other_devices()
    {
        using var client = await api.BearerClientAsync();
        var book = await api.CreateBookAsync(3600);

        var done = await PostAsync(client, Report(book, 3600, Phone, finished: true));
        Assert.True(done.Progress.IsFinished);

        var stale = await PostAsync(client, Report(book, 3000, Laptop));
        Assert.False(stale.Accepted);
        Assert.Equal(ProgressReason.BehindFinished, stale.Reason);
        Assert.True(stale.Progress.IsFinished);
    }

    [Fact]
    public async Task An_unknown_book_is_404()
    {
        using var client = await api.BearerClientAsync();
        var response = await client.PostAsJsonAsync("/api/progress", Report(Guid.NewGuid(), 10, Laptop), ApiFixture.Json);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Concurrent_first_reports_do_not_fail()
    {
        // Both see no row and try to insert; the loser must retry, not 500.
        using var client = await api.BearerClientAsync();
        var book = await api.CreateBookAsync(3600);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            client.PostAsJsonAsync("/api/progress", Report(book, 100 + i, Guid.NewGuid()), ApiFixture.Json)));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var stored = await client.GetFromJsonAsync<ProgressDto>($"/api/books/{book}/progress", ApiFixture.Json);
        Assert.Equal(107, stored!.PositionSeconds);
    }
}
