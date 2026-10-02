using AudiobookServer.Core.Scanning;

namespace AudiobookServer.Tests;

/// <summary>Which folders become books. Against real (empty) files in a temporary folder.</summary>
public sealed class LibraryWalkerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("audiobook-walker").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private void Touch(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, []);
    }

    private List<BookCandidate> Walk() => new LibraryWalker().FindBooks(_root).ToList();

    private string Relative(string path) => Path.GetRelativePath(_root, path).Replace('\\', '/');

    [Fact]
    public void A_folder_of_audio_is_one_book()
    {
        Touch("Book/01.mp3");
        Touch("Book/02.mp3");

        var book = Assert.Single(Walk());
        Assert.Equal("Book", Relative(book.DirectoryPath));
        Assert.Equal(2, book.FilePaths.Count);
    }

    [Fact]
    public void A_disc_set_is_one_book_in_disc_order()
    {
        Touch("Norwegian Wood/cd 01 of 11/01.mp3");
        Touch("Norwegian Wood/cd 01 of 11/02.mp3");
        Touch("Norwegian Wood/cd 02 of 11/01.mp3");
        Touch("Norwegian Wood/cd 10 of 11/01.mp3");

        var book = Assert.Single(Walk());

        Assert.Equal("Norwegian Wood", Relative(book.DirectoryPath));
        Assert.Equal(
            ["Norwegian Wood/cd 01 of 11/01.mp3", "Norwegian Wood/cd 01 of 11/02.mp3",
             "Norwegian Wood/cd 02 of 11/01.mp3", "Norwegian Wood/cd 10 of 11/01.mp3"],
            book.FilePaths.Select(Relative));
    }

    [Theory]
    [InlineData("disc 1", "disc 2")]
    [InlineData("Disc 1", "Disc 10")]
    [InlineData("CD1", "CD2")]
    [InlineData("disk_1", "disk_2")]
    public void Disc_folder_names_are_recognised(string first, string second)
    {
        Touch($"Book/{first}/a.mp3");
        Touch($"Book/{second}/a.mp3");

        var book = Assert.Single(Walk());
        Assert.Equal("Book", Relative(book.DirectoryPath));
    }

    [Fact]
    public void Folders_of_separate_books_stay_separate()
    {
        Touch("Series/Book One/a.mp3");
        Touch("Series/Book Two/a.mp3");

        Assert.Equal(["Series/Book One", "Series/Book Two"], Walk().Select(b => Relative(b.DirectoryPath)).Order());
    }

    [Fact]
    public void A_mixed_folder_is_not_taken_for_a_disc_set()
    {
        // Only a clean set merges; anything else keeps one book per folder.
        Touch("Odd/disc 1/a.mp3");
        Touch("Odd/Extras/b.mp3");

        Assert.Equal(["Odd/disc 1", "Odd/Extras"], Walk().Select(b => Relative(b.DirectoryPath)).Order());
    }

    [Fact]
    public void Part_folders_are_not_discs()
    {
        Touch("Saga/Part 1/a.mp3");
        Touch("Saga/Part 2/a.mp3");

        Assert.Equal(2, Walk().Count);
    }
}
