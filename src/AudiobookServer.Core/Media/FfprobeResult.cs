using System.Text.Json.Serialization;

namespace AudiobookServer.Core.Media;

/// <summary>
/// Deserialization target for ffprobe's JSON output. Covers both the main probe
/// (-show_format -show_chapters -show_streams) and the mp3 packet count
/// (-count_packets -show_entries stream=...), which fill different parts of it.
/// </summary>
public class FfprobeOutput
{
    [JsonPropertyName("format")]
    public FfprobeFormat? Format { get; set; }

    [JsonPropertyName("chapters")]
    public List<FfprobeChapter> Chapters { get; set; } = [];

    /// <summary>
    /// Only the first audio stream, because every call passes -select_streams a:0.
    /// Without that, embedded cover art (an mjpeg "video" stream) would appear here too.
    /// </summary>
    [JsonPropertyName("streams")]
    public List<FfprobeStream> Streams { get; set; } = [];
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

    /// <summary>
    /// How confident ffprobe is in what it parsed. 100 is certain; mp3 often reports ~51.
    /// It does not indicate duration accuracy: an exact mp3 and one off by 0.15% scored 51 and 52.
    /// </summary>
    [JsonPropertyName("probe_score")]
    public int ProbeScore { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class FfprobeStream
{
    [JsonPropertyName("codec_name")]
    public string? CodecName { get; set; }

    [JsonPropertyName("codec_type")]
    public string? CodecType { get; set; }

    [JsonPropertyName("sample_rate")]
    public int? SampleRate { get; set; }

    /// <summary>Only present when ffprobe ran with -count_packets.</summary>
    [JsonPropertyName("nb_read_packets")]
    public long? NbReadPackets { get; set; }
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
