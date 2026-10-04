using AudiobookServer.Core.Entities;
using AudiobookServer.Core.Scanning;

namespace AudiobookServer.Tests;

public class ContentVersionTests
{
    private static readonly DateTimeOffset Modified = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static AudioFile File(int sequence, string path = "", long size = 1000, double duration = 60) => new()
    {
        Id = Guid.NewGuid(),
        Sequence = sequence,
        RelativePath = path == "" ? $"Book/{sequence:00}.mp3" : path,
        SizeBytes = size,
        DurationSeconds = duration,
        FileModifiedAt = Modified,
    };

    private static string Version(params AudioFile[] files) => ContentVersion.For(files);

    [Fact]
    public void Is_stable_for_the_same_files_and_ignores_row_identity_and_list_order()
    {
        // A rescan of an unchanged book makes new rows (new IDs) and may list them in
        // any order; neither is a change a download cares about.
        Assert.Equal(Version(File(0), File(1)), Version(File(1), File(0)));
        Assert.Matches("^[0-9a-f]{16}$", Version(File(0)));
    }

    [Fact]
    public void Ignores_differences_below_a_millisecond()
    {
        // Postgres keeps microseconds; the scanner sees 100ns ticks. A stored row and a
        // fresh scan of the same file must agree.
        var fresh = File(0);
        fresh.FileModifiedAt = Modified.AddTicks(1234);
        Assert.Equal(Version(File(0)), Version(fresh));
    }

    public static TheoryData<string, AudioFile[]> Changes => new()
    {
        { "size", [File(0), File(1, size: 999)] },
        { "duration", [File(0), File(1, duration: 60.5)] },
        { "path", [File(0), File(1, path: "Book/renamed.mp3")] },
        { "a file added", [File(0), File(1), File(2)] },
        { "a file removed", [File(0)] },
        { "files swapped", [File(0, "Book/01.mp3"), File(1, "Book/00.mp3")] },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void Changes_when_a_download_would_no_longer_match(string change, AudioFile[] files)
    {
        Assert.NotEqual(Version(File(0), File(1)), Version(files));
        _ = change;
    }

    [Fact]
    public void Changes_with_the_modification_time()
    {
        var touched = File(1);
        touched.FileModifiedAt = Modified.AddSeconds(1);
        Assert.NotEqual(Version(File(0), File(1)), Version(File(0), touched));
    }
}
