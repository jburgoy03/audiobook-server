using AudiobookServer.Core.Media;

namespace AudiobookServer.Tests;

public class Mp3FramesTests
{
    [Theory]
    [InlineData(48000, 1152)]
    [InlineData(44100, 1152)]
    [InlineData(32000, 1152)] // MPEG-1, despite sitting on the old "above 32 kHz" boundary
    [InlineData(24000, 576)]
    [InlineData(22050, 576)]
    [InlineData(16000, 576)]
    [InlineData(12000, 576)]
    [InlineData(11025, 576)]
    [InlineData(8000, 576)]
    public void Samples_per_frame_follows_the_MPEG_version(int sampleRate, int expected) =>
        Assert.Equal(expected, Mp3Frames.SamplesPerFrame(sampleRate));

    [Theory]
    [InlineData(0)]
    [InlineData(96000)]
    [InlineData(44000)]
    public void Unknown_sample_rates_return_null(int sampleRate) =>
        Assert.Null(Mp3Frames.SamplesPerFrame(sampleRate));

    // Both expectations are the measured counts from the investigation.
    [Theory]
    [InlineData(104166, 44100, 2721.0710)] // The Name of the Wind, file 1
    [InlineData(165798, 22050, 4331.0498)] // The Wise Man's Fear, file 01
    public void Duration_from_frame_count(long frames, int sampleRate, double expected) =>
        Assert.Equal(expected, Mp3Frames.DurationSeconds(frames, sampleRate)!.Value, precision: 3);

    [Fact]
    public void Zero_frames_is_not_a_duration() =>
        Assert.Null(Mp3Frames.DurationSeconds(0, 44100));
}
