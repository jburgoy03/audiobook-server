using AudiobookServer.Core.Metadata;

namespace AudiobookServer.Tests;

public class MetadataOverrideTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_edit_clears_the_override(string? edited)
    {
        Assert.Equal((true, null), MetadataOverride.Normalize(edited, "Scanned", 500));
    }

    [Fact]
    public void An_edit_is_trimmed()
    {
        Assert.Equal((true, "After the Quake"), MetadataOverride.Normalize("  After the Quake ", "after the quake", 500));
    }

    [Fact]
    public void An_edit_equal_to_the_scanned_value_is_no_override()
    {
        Assert.Equal((true, null), MetadataOverride.Normalize("Dune ", "Dune", 500));
    }

    [Fact]
    public void Case_alone_is_a_real_change()
    {
        // After the Quake's scanned title differs from the right one only in case.
        Assert.Equal((true, "After the Quake"), MetadataOverride.Normalize("After the Quake", "after the quake", 500));
    }

    [Fact]
    public void A_book_with_no_scanned_author_can_be_given_one()
    {
        Assert.Equal((true, "Haruki Murakami"), MetadataOverride.Normalize("Haruki Murakami", null, 300));
    }

    [Fact]
    public void Too_long_fails_rather_than_truncating()
    {
        Assert.Equal((false, null), MetadataOverride.Normalize(new string('x', 301), "Scanned", 300));
        Assert.Equal((true, new string('x', 300)), MetadataOverride.Normalize(new string('x', 300), "Scanned", 300));
    }
}
