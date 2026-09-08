using AudiobookServer.Core.Entities;
using AudiobookServer.Core.Playback;

namespace AudiobookServer.Tests;

public class BookTimelineTests
{
    /// <summary>Builds a contiguous file list from durations, starting at zero.</summary>
    private static List<AudioFile> Files(params double[] durations)
    {
        var files = new List<AudioFile>();
        var offset = 0d;
        for (var i = 0; i < durations.Length; i++)
        {
            files.Add(new AudioFile
            {
                RelativePath = $"book/{i:D2}.mp3",
                Sequence = i,
                StartOffsetSeconds = offset,
                DurationSeconds = durations[i],
            });
            offset += durations[i];
        }
        return files;
    }

    [Fact]
    public void Resolves_zero_to_the_start_of_the_first_file()
    {
        var files = Files(100, 100, 100);

        var location = BookTimeline.Resolve(files, 0);

        Assert.NotNull(location);
        Assert.Equal(0, location.File.Sequence);
        Assert.Equal(0, location.OffsetInFileSeconds);
    }

    [Fact]
    public void Resolves_a_point_inside_a_middle_file()
    {
        var files = Files(100, 100, 100);

        var location = BookTimeline.Resolve(files, 150);

        Assert.NotNull(location);
        Assert.Equal(1, location.File.Sequence);
        Assert.Equal(50, location.OffsetInFileSeconds);
    }

    [Fact]
    public void Resolves_an_exact_boundary_to_the_start_of_the_later_file()
    {
        // The half-open interval decision: 100 belongs to file 1 at offset 0,
        // not to file 0 at offset 100. Chapter boundaries land here constantly.
        var files = Files(100, 100, 100);

        var location = BookTimeline.Resolve(files, 100);

        Assert.NotNull(location);
        Assert.Equal(1, location.File.Sequence);
        Assert.Equal(0, location.OffsetInFileSeconds);
    }

    [Fact]
    public void Resolves_the_last_moment_of_the_book()
    {
        var files = Files(100, 100, 100);

        var location = BookTimeline.Resolve(files, 299.5);

        Assert.NotNull(location);
        Assert.Equal(2, location.File.Sequence);
        Assert.Equal(99.5, location.OffsetInFileSeconds, precision: 6);
    }

    [Fact]
    public void Book_duration_itself_is_out_of_range()
    {
        // A finished book. Half-open intervals make the end exclusive, so this is
        // deliberately not seekable. Phase 2f must treat "finished" as its own state.
        var files = Files(100, 100, 100);

        Assert.Null(BookTimeline.Resolve(files, 300));
    }

    [Fact]
    public void Past_the_end_is_out_of_range()
    {
        var files = Files(100, 100, 100);

        Assert.Null(BookTimeline.Resolve(files, 5000));
    }

    [Fact]
    public void Negative_offsets_are_out_of_range()
    {
        Assert.Null(BookTimeline.Resolve(Files(100), -1));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_offsets_are_out_of_range(double offset)
    {
        Assert.Null(BookTimeline.Resolve(Files(100, 100), offset));
    }

    [Fact]
    public void An_empty_book_resolves_nothing()
    {
        Assert.Null(BookTimeline.Resolve([], 0));
    }

    [Fact]
    public void A_single_file_book_resolves_across_its_whole_span()
    {
        var files = Files(3600);

        var location = BookTimeline.Resolve(files, 1800);

        Assert.NotNull(location);
        Assert.Equal(0, location.File.Sequence);
        Assert.Equal(1800, location.OffsetInFileSeconds);
    }

    [Fact]
    public void Files_of_unequal_length_resolve_correctly()
    {
        // Guards against an implementation that assumes uniform duration.
        var files = Files(30, 500, 7);

        var location = BookTimeline.Resolve(files, 531);

        Assert.NotNull(location);
        Assert.Equal(2, location.File.Sequence);
        Assert.Equal(1, location.OffsetInFileSeconds, precision: 6);
    }

    [Fact]
    public void An_offset_inside_a_gap_is_out_of_range_rather_than_clamped()
    {
        // The scanner's contiguity invariant should prevent gaps, so this is a
        // corrupt-data path. It must fail visibly rather than snap to nearby audio:
        // silently serving the wrong position is worse than refusing.
        var files = new List<AudioFile>
        {
            new() { RelativePath = "a.mp3", Sequence = 0, StartOffsetSeconds = 0, DurationSeconds = 100 },
            new() { RelativePath = "b.mp3", Sequence = 1, StartOffsetSeconds = 200, DurationSeconds = 100 },
        };

        Assert.Null(BookTimeline.Resolve(files, 150));
        Assert.NotNull(BookTimeline.Resolve(files, 200));
    }

    [Fact]
    public void Overlapping_files_resolve_to_the_later_one()
    {
        // Also corrupt data, but unlike a gap there is a defensible answer, so this
        // documents the behaviour rather than asserting it is correct: the scan is
        // the thing to fix. The last file whose start is at or before the offset wins.
        var files = new List<AudioFile>
        {
            new() { RelativePath = "a.mp3", Sequence = 0, StartOffsetSeconds = 0, DurationSeconds = 100 },
            new() { RelativePath = "b.mp3", Sequence = 1, StartOffsetSeconds = 50, DurationSeconds = 100 },
        };

        var location = BookTimeline.Resolve(files, 75);

        Assert.NotNull(location);
        Assert.Equal(1, location.File.Sequence);
        Assert.Equal(25, location.OffsetInFileSeconds);
    }
}