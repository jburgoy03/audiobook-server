namespace AudiobookServer.Core.Media;

/// <summary>
/// Frame arithmetic for MPEG audio Layer III. ffmpeg reports Layers I and II as
/// mp1/mp2, so codec_name "mp3" guarantees Layer III and these numbers apply.
/// </summary>
public static class Mp3Frames
{
    /// <summary>
    /// Samples per Layer III frame, decided by the MPEG version, which the sample
    /// rate identifies. Matches the nine legal rates explicitly rather than using a
    /// threshold: 32 kHz is MPEG-1, so an "above 32 kHz" rule would get it wrong.
    /// Returns null for anything else, so the caller falls back to the header.
    /// </summary>
    public static int? SamplesPerFrame(int sampleRate) => sampleRate switch
    {
        32000 or 44100 or 48000 => 1152,  // MPEG-1
        16000 or 22050 or 24000 => 576,   // MPEG-2
        8000 or 11025 or 12000 => 576,    // MPEG-2.5
        _ => null
    };

    /// <summary>
    /// Duration from a frame count. Includes the encoder's delay and padding samples,
    /// so it runs up to ~30ms longer than the audible audio. That is consistent per
    /// file and far below what matters for the timeline.
    /// </summary>
    public static double? DurationSeconds(long frames, int sampleRate) =>
        frames > 0 && SamplesPerFrame(sampleRate) is { } samples
            ? frames * (double)samples / sampleRate
            : null;
}
