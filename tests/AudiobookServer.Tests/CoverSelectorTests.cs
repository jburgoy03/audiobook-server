using AudiobookServer.Core.Media;

namespace AudiobookServer.Tests;

public class CoverSelectorTests
{
    private static FolderImage Image(string name, int size) => new($"/library/book/{name}", "jpeg", size, size);

    private static EmbeddedCover Embedded(int size) =>
        new("/library/book/01.mp3", new ProbedCover(1, "mjpeg", size, size));

    [Fact]
    public void A_larger_folder_image_beats_the_embedded_picture()
    {
        var cover = Image("cover.jpg", 1400);
        Assert.Same(cover, CoverSelector.Choose([cover], Embedded(500), folderIsCollection: false));
    }

    [Fact]
    public void A_larger_embedded_picture_beats_a_folder_thumbnail()
    {
        var embedded = Embedded(1000);
        Assert.Same(embedded, CoverSelector.Choose([Image("cover.jpg", 300)], embedded, folderIsCollection: false));
    }

    [Fact]
    public void The_largest_of_several_folder_images_wins_whatever_its_name()
    {
        var large = Image("Pride and Prejudice 1209.jpg", 1400);
        var chosen = CoverSelector.Choose([Image("folder.jpg", 600), large, Image("thumb.jpg", 180)], null, false);
        Assert.Same(large, chosen);
    }

    [Theory]
    [InlineData("back.jpg")]
    [InlineData("Back Cover.jpg")]
    [InlineData("cd1.jpg")]
    [InlineData("disc 2.png")]
    [InlineData("inlay.jpg")]
    [InlineData("booklet_03.jpg")]
    public void Images_named_like_backs_or_discs_are_never_chosen(string name)
    {
        var front = Image("cover.jpg", 500);
        Assert.Same(front, CoverSelector.Choose([Image(name, 3000), front], null, false));
    }

    [Fact]
    public void Back_as_part_of_a_longer_word_is_not_excluded()
    {
        var image = Image("Backstory.jpg", 800);
        Assert.Same(image, CoverSelector.Choose([image], null, false));
    }

    [Fact]
    public void Ties_go_to_a_cover_named_image_then_other_folder_images_then_embedded()
    {
        var named = Image("cover.jpg", 600);
        var other = Image("art.jpg", 600);
        var embedded = Embedded(600);

        Assert.Same(named, CoverSelector.Choose([other, named], embedded, false));
        Assert.Same(other, CoverSelector.Choose([other], embedded, false));
    }

    [Fact]
    public void Ties_between_unnamed_images_are_settled_by_file_name_not_order()
    {
        var a = Image("a.jpg", 600);
        var b = Image("b.jpg", 600);
        Assert.Same(a, CoverSelector.Choose([b, a], null, false));
        Assert.Same(a, CoverSelector.Choose([a, b], null, false));
    }

    [Fact]
    public void In_a_collection_a_book_keeps_its_own_embedded_art()
    {
        var embedded = Embedded(300);
        Assert.Same(embedded, CoverSelector.Choose([Image("cover.jpg", 3000)], embedded, folderIsCollection: true));
    }

    [Fact]
    public void In_a_collection_folder_art_fills_in_for_a_book_with_none()
    {
        var cover = Image("cover.jpg", 3000);
        Assert.Same(cover, CoverSelector.Choose([cover], null, folderIsCollection: true));
    }

    [Fact]
    public void An_embedded_picture_of_unknown_size_still_beats_nothing()
    {
        var embedded = Embedded(0);
        Assert.Same(embedded, CoverSelector.Choose([], embedded, false));
    }

    [Fact]
    public void Nothing_to_choose_from_is_null() =>
        Assert.Null(CoverSelector.Choose([], null, false));
}
