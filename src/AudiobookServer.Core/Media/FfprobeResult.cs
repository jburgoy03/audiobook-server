using System.Text.Json.Serialization;

namespace AudiobookServer.Core.Media;

/// <summary>Deserialization target for `ffprobe -print_format json -show_format -show_chapters`.</summary>
public class FfprobeOutput
{
    [JsonPropertyName("format")]
    public FfprobeFormat? Format { get; set; }

    [JsonPropertyName("chapters")]
    public List<FfprobeChapter> Chapters { get; set; } = [];
}

public class FfprobeFormat
{
    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("format_name")]
    public string? FormatName { get; set; }

    /// <summary>Seconds, as a string. ffprobe emits numbers as strings here.</summary>
    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    [JsonPropertyName("size")]
    public string? Size { get; set; }

    [JsonPropertyName("bit_rate")]
    public string? BitRate { get; set; }

    /// <summary>How confident ffprobe is in what it parsed. 100 is certain; mp3 often reports ~51.</summary>
    [JsonPropertyName("probe_score")]
    public int ProbeScore { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class FfprobeChapter
{
    [JsonPropertyName("start_time")]
    public string? StartTime { get; set; }

    [JsonPropertyName("end_time")]
    public string? EndTime { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}