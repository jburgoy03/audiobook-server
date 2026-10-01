using AudiobookServer.Core.Media;

namespace AudiobookServer.Tests;

public class MediaProbeTests
{
    // The assertions below are specific to these exact files. Set AUDIOBOOK_SAMPLE_M4B
    // and AUDIOBOOK_SAMPLE_MP3 to local copies to run the tests off the server;
    // otherwise they skip.
    private static readonly string M4bPath =
        Environment.GetEnvironmentVariable("AUDIOBOOK_SAMPLE_M4B")
        ?? "/mnt/media/audiobooks/The Lies of Locke Lamora - Scott Lynch m4b/01 00_ Prologue - The Boy Who Stole Too Much 01.m4b";

    // The Name of the Wind, file 1: an encoder that never sets the padding bit.
    private static readonly string? Mp3Path =
        Environment.GetEnvironmentVariable("AUDIOBOOK_SAMPLE_MP3");

    [SkippableFact]
    public async Task Probes_an_m4b_file()
    {
        Skip.IfNot(File.Exists(M4bPath), "Sample file not present.");

        var probe = new FfprobeMediaProbe();
        var result = await probe.ProbeAsync(M4bPath);

        Assert.Equal(4569.953344, result.DurationSeconds, precision: 3);
        Assert.Equal(DurationSource.Header, result.DurationSource);
        Assert.Equal("Scott Lynch", result.Tag("artist"));
        Assert.Equal("1/21", result.Tag("track"));
        Assert.Single(result.Chapters);
        Assert.Equal("00: Prologue - The Boy Who Stole Too Much 01", result.Chapters[0].Title);
    }

    [SkippableFact]
    public async Task Counts_packets_for_an_unpadded_mp3()
    {
        Skip.If(Mp3Path is null || !File.Exists(Mp3Path), "Sample mp3 not configured.");

        var probe = new FfprobeMediaProbe();
        var result = await probe.ProbeAsync(Mp3Path!);

        Assert.Equal("mp3", result.Codec);
        Assert.Equal(DurationSource.PacketCount, result.DurationSource);
        Assert.Equal(2721.071, result.DurationSeconds, precision: 2);
        Assert.Equal(2717.007, result.HeaderDurationSeconds!.Value, precision: 2);
    }

    [Fact]
    public async Task Throws_on_a_missing_file()
    {
        var probe = new FfprobeMediaProbe();
        await Assert.ThrowsAsync<MediaProbeException>(
            () => probe.ProbeAsync(Path.Combine(Path.GetTempPath(), "definitely-not-here.m4b")));
    }
}
