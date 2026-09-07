using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace AudiobookServer.Core.Media;

/// <summary>What the scanner needs to know about one audio file on disk.</summary>
public record ProbedFile(
    string Path,
    double DurationSeconds,
    long SizeBytes,
    int? Bitrate,
    string? FormatName,
    int ProbeScore,
    IReadOnlyDictionary<string, string> Tags,
    IReadOnlyList<ProbedChapter> Chapters)
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
        PropertyNameCaseInsensitive = true
    };

    public async Task<ProbedFile> ProbeAsync(string path, CancellationToken ct = default)
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
        psi.ArgumentList.Add("-print_format");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add("-show_format");
        psi.ArgumentList.Add("-show_chapters");
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

        var parsed = JsonSerializer.Deserialize<FfprobeOutput>(stdout, JsonOptions)
            ?? throw new MediaProbeException($"ffprobe returned no parseable output for '{path}'.");

        if (parsed.Format is null)
            throw new MediaProbeException($"ffprobe returned no format block for '{path}'.");

        var chapters = parsed.Chapters
            .Select(c => new ProbedChapter(
                c.Tags.TryGetValue("title", out var t) ? t.Trim() : null,
                ParseSeconds(c.StartTime),
                ParseSeconds(c.EndTime)))
            .Where(c => c.EndSeconds > c.StartSeconds)
            .ToList();

        return new ProbedFile(
            Path: path,
            DurationSeconds: ParseSeconds(parsed.Format.Duration),
            SizeBytes: ParseLong(parsed.Format.Size),
            Bitrate: ParseInt(parsed.Format.BitRate),
            FormatName: parsed.Format.FormatName,
            ProbeScore: parsed.Format.ProbeScore,
            Tags: parsed.Format.Tags,
            Chapters: chapters);
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