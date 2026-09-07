using AudiobookServer.Core.Media;
using AudiobookServer.Core.Scanning;

namespace AudiobookServer.Tests;

public class BookScannerTests
{
    private const string Root = "/library";

    /// <summary>Returns canned probe results keyed by path, so no audio files are needed.</summary>
    private sealed class FakeProbe(Dictionary<string, ProbedFile> results) : IMediaProbe
    {
        public Task<ProbedFile> ProbeAsync(string path, CancellationToken ct = default) =>
            results.TryGetValue(path, out var r)
                ? Task.FromResult(r)
                : throw new MediaProbeException($"no fake result for {path}");
    }

    private static ProbedFile File(
        string path,
        double duration,
        string? track = null,
        string? album = null,
        string? artist = null,
        string? title = null,
        params ProbedChapter[] chapters)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (track is not null) tags["track"] = track;
        if (album is not null) tags["album"] = album;
        if (artist is not null) tags["artist"] = artist;
        if (title is not null) tags["title"] = title;

        return new ProbedFile(path, duration, 1000, 64000, "mp3", 100, tags, chapters);
    }

    private static BookScanner ScannerFor(params ProbedFile[] files) =>
        new(new FakeProbe(files.ToDictionary(f => f.Path)));

    [Fact]
    public async Task Places_files_end_to_end_on_one_timeline()
    {
        var a = File("/library/book/a.mp3", 100, track: "1");
        var b = File("/library/book/b.mp3", 250, track: "2");
        var c = File("/library/book/c.mp3", 50, track: "3");

        var result = await ScannerFor(a, b, c)
            .ScanAsync(new BookCandidate("/library/book", [a.Path, b.Path, c.Path]), Root);

        Assert.NotNull(result);
        Assert.Equal(400, result.DurationSeconds);
        Assert.Equal([0, 100, 350], result.Files.Select(f => f.StartOffsetSeconds));
        Assert.Equal([0, 1, 2], result.Files.Select(f => f.Sequence));
    }

    [Fact]
    public async Task Orders_by_track_tag_even_when_filenames_disagree()
    {
        // Filenames sort z, y, x — track tags say otherwise, and they win.
        var x = File("/library/book/z.mp3", 10, track: "1");
        var y = File("/library/book/y.mp3", 10, track: "2");
        var z = File("/library/book/x.mp3", 10, track: "3");

        var result = await ScannerFor(x, y, z)
            .ScanAsync(new BookCandidate("/library/book", [x.Path, y.Path, z.Path]), Root);

        Assert.Equal(["z.mp3", "y.mp3", "x.mp3"], result!.Files.Select(f => Path.GetFileName(f.RelativePath)));
    }

    [Fact]
    public async Task Falls_back_to_natural_sort_when_track_tags_are_missing()
    {
        // Unpadded numbering: lexicographic sort would put 10 before 2.
        var one = File("/library/book/Chapter 1.mp3", 10);
        var two = File("/library/book/Chapter 2.mp3", 10);
        var ten = File("/library/book/Chapter 10.mp3", 10);

        var result = await ScannerFor(one, two, ten)
            .ScanAsync(new BookCandidate("/library/book", [one.Path, ten.Path, two.Path]), Root);

        Assert.Equal(
            ["Chapter 1.mp3", "Chapter 2.mp3", "Chapter 10.mp3"],
            result!.Files.Select(f => Path.GetFileName(f.RelativePath)));
    }

    [Fact]
    public async Task Shifts_embedded_chapters_onto_the_book_timeline()
    {
        var a = File("/library/book/a.m4b", 100, track: "1",
            chapters: [new ProbedChapter("One", 0, 60), new ProbedChapter("Two", 60, 100)]);
        var b = File("/library/book/b.m4b", 80, track: "2",
            chapters: [new ProbedChapter("Three", 0, 80)]);

        var result = await ScannerFor(a, b)
            .ScanAsync(new BookCandidate("/library/book", [a.Path, b.Path]), Root);

        Assert.Equal(["One", "Two", "Three"], result!.Chapters.Select(c => c.Title));
        Assert.Equal([0, 60, 100], result.Chapters.Select(c => c.StartOffsetSeconds));
        Assert.Equal([60, 100, 180], result.Chapters.Select(c => c.EndOffsetSeconds));
    }

    [Fact]
    public async Task Synthesises_one_chapter_per_file_when_none_are_embedded()
    {
        var a = File("/library/book/a.mp3", 100, track: "1", title: "Ch01: A Place for Demons");
        var b = File("/library/book/b.mp3", 100, track: "2", title: "Ch02: A Beautiful Day");

        var result = await ScannerFor(a, b)
            .ScanAsync(new BookCandidate("/library/book", [a.Path, b.Path]), Root);

        Assert.Equal(2, result!.Chapters.Count);
        Assert.Equal("Ch01: A Place for Demons", result.Chapters[0].Title);
        Assert.Equal(100, result.Chapters[0].EndOffsetSeconds);
        Assert.Equal(100, result.Chapters[1].StartOffsetSeconds);
    }

    [Fact]
    public async Task Chapters_are_contiguous_across_the_whole_book()
    {
        var a = File("/library/book/a.mp3", 33.3, track: "1");
        var b = File("/library/book/b.mp3", 66.7, track: "2");

        var result = await ScannerFor(a, b)
            .ScanAsync(new BookCandidate("/library/book", [a.Path, b.Path]), Root);

        // No gaps and no overlaps: every chapter starts where the previous one ended.
        var chapters = result!.Chapters;
        for (var i = 1; i < chapters.Count; i++)
            Assert.Equal(chapters[i - 1].EndOffsetSeconds, chapters[i].StartOffsetSeconds, precision: 6);

        Assert.Equal(result.DurationSeconds, chapters[^1].EndOffsetSeconds, precision: 6);
    }

    [Fact]
    public async Task Skips_files_the_probe_cannot_read()
    {
        var good = File("/library/book/a.mp3", 100, track: "1");

        var result = await ScannerFor(good)
            .ScanAsync(new BookCandidate("/library/book", [good.Path, "/library/book/broken.mp3"]), Root);

        Assert.Single(result!.Files);
        Assert.Equal(100, result.DurationSeconds);
    }

    [Fact]
    public async Task Returns_null_when_nothing_is_readable()
    {
        var result = await ScannerFor()
            .ScanAsync(new BookCandidate("/library/book", ["/library/book/broken.mp3"]), Root);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("The Name of the Wind by Patrick Rothfuss", "Patrick Rothfuss")]
    [InlineData("The Lies of Locke Lamora - Scott Lynch m4b", "Scott Lynch")]
    [InlineData("Just A Title", null)]
    public async Task Derives_author_from_directory_name_when_tags_are_absent(string dirName, string? expected)
    {
        var file = File($"/library/{dirName}/a.mp3", 10);

        var result = await ScannerFor(file)
            .ScanAsync(new BookCandidate($"/library/{dirName}", [file.Path]), Root);

        Assert.Equal(expected, result!.Author);
    }
}