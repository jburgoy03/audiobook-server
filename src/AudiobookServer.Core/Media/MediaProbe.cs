using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudiobookServer.Core.Media;

/// <summary>Where a file's duration came from.</summary>
/// <remarks>Persisted as an integer on AudioFile. Append new values; never renumber.</remarks>
public enum DurationSource
{
    /// <summary>ffprobe's format.duration. Exact for m4b; an estimate for mp3.</summary>
    Header = 0,

    /// <summary>mp3 packets counted and multiplied by samples per frame.</summary>
    PacketCount = 1
}

/// <summary>What the scanner needs to know about one audio file on disk.</summary>
public record ProbedFile(
    string Path,
    double DurationSeconds,
    long SizeBytes,
    int? Bitrate,
    string? FormatName,
    int ProbeScore,
    IReadOnlyDictionary<string, string> Tags,
    IReadOnlyList<ProbedChapter> Chapters,
    string? Codec = null,
    double? HeaderDurationSeconds = null,
    DurationSource DurationSource = DurationSource.Header)
{
    public string? Tag(string name) =>
        Tags.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
}

/// <summary>A chapter as it exists inside one file, with offsets relative to that file's start.</summary>
public record ProbedChapter(string? Title, double StartSeconds, double EndSeconds);

public interface IMediaProbe
{
    Task<ProbedFile> ProbeAsync(string path, CancellationToken ct = default);
}

public class FfprobeMediaProbe(string executable = "ffprobe") : IMediaProbe
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // ffprobe writes some numbers as JSON strings and some as numbers. This lets
        // the numeric stream fields accept either form.
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public async Task<ProbedFile> ProbeAsync(string path, CancellationToken ct = default)
    {
        // -select_streams a:0 keeps cover art out of the streams array. It does not
        // affect -show_format or -show_chapters.
        var parsed = await RunAsync(path, ct,
            "-print_format", "json",
            "-show_format", "-show_chapters",
            "-show_streams", "-select_streams", "a:0");

        if (parsed.Format is null)
            throw new MediaProbeException($"ffprobe returned no format block for '{path}'.");

        var codec = parsed.Streams.FirstOrDefault()?.CodecName;
        var headerDuration = ParseSeconds(parsed.Format.Duration);

        var duration = headerDuration;
        var source = DurationSource.Header;

        // mp3 header durations are size ÷ bitrate estimates. Encoders that never set
        // the padding bit make that estimate ~0.15% short, and nothing in the headers
        // reveals it, so every mp3 gets counted. m4b durations come from the
        // container's sample tables and are already exact.
        if (string.Equals(codec, "mp3", StringComparison.OrdinalIgnoreCase)
            && await CountMp3DurationAsync(path, ct) is { } counted)
        {
            duration = counted;
            source = DurationSource.PacketCount;
        }

        var chapters = parsed.Chapters
            .Select(c => new ProbedChapter(
                c.Tags.TryGetValue("title", out var t) ? t.Trim() : null,
                ParseSeconds(c.StartTime),
                ParseSeconds(c.EndTime)))
            .Where(c => c.EndSeconds > c.StartSeconds)
            .ToList();

        return new ProbedFile(
            Path: path,
            DurationSeconds: duration,
            SizeBytes: ParseLong(parsed.Format.Size),
            Bitrate: ParseInt(parsed.Format.BitRate),
            FormatName: parsed.Format.FormatName,
            ProbeScore: parsed.Format.ProbeScore,
            Tags: parsed.Format.Tags,
            Chapters: chapters,
            Codec: codec,
            HeaderDurationSeconds: headerDuration,
            DurationSource: source);
    }

    /// <summary>
    /// Counts packets rather than frames. The mp3 demuxer splits on frame headers, so
    /// one packet is one frame (verified on MPEG-1 and MPEG-2 files), and counting
    /// packets skips decoding: ~9x faster than -count_frames for the same number.
    /// Returns null on any failure so the caller keeps the header duration.
    /// </summary>
    private async Task<double?> CountMp3DurationAsync(string path, CancellationToken ct)
    {
        try
        {
            var parsed = await RunAsync(path, ct,
                "-print_format", "json",
                "-select_streams", "a:0",
                "-count_packets",
                "-show_entries", "stream=sample_rate,nb_read_packets");

            var stream = parsed.Streams.FirstOrDefault();
            if (stream?.NbReadPackets is not { } packets || stream.SampleRate is not { } sampleRate)
                return null;

            return Mp3Frames.DurationSeconds(packets, sampleRate);
        }
        catch (MediaProbeException)
        {
            return null;
        }
    }

    private async Task<FfprobeOutput> RunAsync(string path, CancellationToken ct, params string[] args)
    {
        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // ArgumentList escapes each item, so paths with spaces and quotes are safe.
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.ArgumentList.Add(path);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start {executable}.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new MediaProbeException($"ffprobe exited {process.ExitCode} for '{path}': {stderr.Trim()}");

        try
        {
            return JsonSerializer.Deserialize<FfprobeOutput>(stdout, JsonOptions)
                ?? throw new MediaProbeException($"ffprobe returned no parseable output for '{path}'.");
        }
        catch (JsonException ex)
        {
            throw new MediaProbeException($"ffprobe output for '{path}' was not valid JSON: {ex.Message}");
        }
    }

    // ffprobe always emits these as invariant-culture strings, never localized.
    private static double ParseSeconds(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    private static long ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0;

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
}

public class MediaProbeException(string message) : Exception(message);
