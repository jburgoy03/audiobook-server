using System.Text.Json;
using AudiobookServer.Core.Metadata;

namespace AudiobookServer.Tests;

public class BlurbTextTests
{
    [Theory]
    [InlineData("After the Quake", "After the Quake")]
    [InlineData("After the Quake", "after the quake: Stories")]
    [InlineData("Blind Willow, Sleeping Woman", "Blind Willow, Sleeping Woman: Twenty-Four Stories")]
    [InlineData("The Will of the Many", "The Will of the Many (Hierarchy, #1)")]
    [InlineData("Pride and Prejudice", "Pride & Prejudice")]
    [InlineData("Dr Jekyll and Mr Hyde", "Dr. Jekyll and Mr. Hyde")]
    [InlineData("The Lies of Locke Lamora", "The Lies of Locke Lamora - Gentleman Bastard 1")]
    public void Titles_that_match(string searched, string found)
    {
        Assert.True(BlurbText.TitleMatches(searched, found));
    }

    [Theory]
    [InlineData("Dune", "Dune Messiah")]
    [InlineData("The Name of the Wind", "The Wise Man's Fear")]
    [InlineData("Frankenstein", null)]
    [InlineData("Frankenstein", "")]
    public void Titles_that_do_not(string searched, string? found)
    {
        Assert.False(BlurbText.TitleMatches(searched, found));
    }

    [Fact]
    public void Reads_an_Open_Library_description_in_either_shape()
    {
        using var plain = JsonDocument.Parse("""{ "description": "Plain." }""");
        using var typed = JsonDocument.Parse("""{ "description": { "type": "/type/text", "value": "Typed." } }""");
        using var none = JsonDocument.Parse("""{ "title": "No description" }""");

        Assert.Equal("Plain.", BlurbText.OpenLibraryDescription(plain.RootElement));
        Assert.Equal("Typed.", BlurbText.OpenLibraryDescription(typed.RootElement));
        Assert.Null(BlurbText.OpenLibraryDescription(none.RootElement));
    }

    [Fact]
    public void Cleans_Open_Library_markdown()
    {
        const string raw =
            "A **young** man becomes the most notorious [wizard](https://en.wikipedia.org/wiki/Wizard) his world has ever seen.\r\n\r\n" +
            "Told in his own voice. ([source][1])\r\n\r\n" +
            "----------\r\nAlso contained in:\r\n\r\n - [Kingkiller Box Set](/works/OL1W)\r\n\r\n" +
            "  [1]: https://example.com/source";

        Assert.Equal(
            "A young man becomes the most notorious wizard his world has ever seen.\n\nTold in his own voice.",
            BlurbText.CleanOpenLibrary(raw));
    }

    [Fact]
    public void Strips_markdown_italics_but_not_stray_asterisks()
    {
        const string raw = "*The Name of the Wind*, also called _The Kingkiller Chronicle: Day One_, is a novel. Rated 5* by snake_case_fans.";
        Assert.Equal(
            "The Name of the Wind, also called The Kingkiller Chronicle: Day One, is a novel. Rated 5* by snake_case_fans.",
            BlurbText.CleanOpenLibrary(raw));
    }

    [Fact]
    public void Drops_reference_definitions_without_a_rule()
    {
        const string raw = "Ten stories set in the weeks after the Kobe earthquake of 1995. ([source][1])\n\n[1]: https://example.com";
        Assert.Equal("Ten stories set in the weeks after the Kobe earthquake of 1995.", BlurbText.CleanOpenLibrary(raw));
    }

    [Fact]
    public void Cleans_Google_html()
    {
        const string raw = "<p><b>The thrilling first book</b> in a new series.</p><p>Rome&#39;s rule, reimagined &amp; retold.</p>";
        Assert.Equal(
            "The thrilling first book in a new series.\n\nRome's rule, reimagined & retold.",
            BlurbText.CleanGoogle(raw));
    }

    [Theory]
    [InlineData("The Name of the Wind, also called The Kingkiller Chronicle: Day One, is a heroic fantasy novel written by American author Patrick Rothfuss. It is the first book in a trilogy.")]
    [InlineData("Pride and Prejudice is an 1813 novel of manners by Jane Austen. The novel follows Elizabeth Bennet.")]
    [InlineData("Strange Case of Dr. Jekyll and Mr. Hyde is a gothic novella by Scottish author Robert Louis Stevenson, first published in 1886.")]
    [InlineData("The Lies of Locke Lamora is a fantasy novel by American writer Scott Lynch. It was published in 2006.")]
    [InlineData("Frankenstein; or, The Modern Prometheus was published in London in 1818, when Mary Shelley was twenty.")]
    public void Encyclopedia_entries_are_not_blurbs(string text)
    {
        Assert.True(BlurbText.LooksEncyclopedic(text));
    }

    [Theory]
    [InlineData("My name is Kvothe. I have stolen princesses back from sleeping barrow kings. You may have heard of me.")]
    [InlineData("An orphan's life is harsh—and often short—in the island city of Camorr. But young Locke Lamora dodges death and slavery.")]
    [InlineData("Dr. Jekyll is a respected man. But who is Mr. Hyde, and why was his story written by a frightened lawyer?")]
    [InlineData("Ten stories set in the weeks after the Kobe earthquake. Later, it was published in English by Knopf.")]
    [InlineData("In this book by J. R. R. Tolkien's son, the tale is told anew. It was written by hand.")]
    public void Blurbs_are_not_encyclopedia_entries(string text)
    {
        Assert.False(BlurbText.LooksEncyclopedic(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("A novel.")]
    public void A_stub_is_no_blurb(string? raw)
    {
        Assert.Null(BlurbText.CleanOpenLibrary(raw));
        Assert.Null(BlurbText.CleanGoogle(raw));
    }
}
