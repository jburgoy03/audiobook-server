using AudiobookServer.Core.Media;

namespace AudiobookServer.Tests;

public class MediaProbeTests
{
    // The assertions below are specific to this exact file. Set AUDIOBOOK_SAMPLE_M4B to a
    // local copy of it to run the test off the server; otherwise it skips.
    private static readonly string M4bPath =
        Environment.GetEnvironmentVariable("AUDIOBOOK_SAMPLE_M4B")
        ?? "/mnt/media/audiobooks/The Lies of Locke Lamora - Scott Lynch m4b/01 00_ Prologue - The Boy Who Stole Too Much 01.m4b";

    [SkippableFact]
    public async Task Probes_an_m4b_file()
    {
        Skip.IfNot(File.Exists(M4bPath), "Sample file not present.");

        var probe = new FfprobeMediaProbe();
        var result = await probe.ProbeAsync(M4bPath);

        Assert.Equal(4569.953344, result.DurationSeconds, precision: 3);
        Assert.Equal("Scott Lynch", result.Tag("artist"));
        Assert.Equal("1/21", result.Tag("track"));
        Assert.Single(result.Chapters);
        Assert.Equal("00: Prologue - The Boy Who Stole Too Much 01", result.Chapters[0].Title);
    }

    [Fact]
    public async Task Throws_on_a_missing_file()
    {
        var probe = new FfprobeMediaProbe();
        await Assert.ThrowsAsync<MediaProbeException>(
            () => probe.ProbeAsync(Path.Combine(Path.GetTempPath(), "definitely-not-here.m4b")));
    }
}