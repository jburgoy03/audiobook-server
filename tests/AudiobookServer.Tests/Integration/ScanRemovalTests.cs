using AudiobookServer.Core.Entities;
using AudiobookServer.Core.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookServer.Tests.Integration;

/// <summary>
/// A book whose folder is gone is removed by the next scan, unless the scan found no
/// book folders at all, which looks the same as an unmounted drive.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class ScanRemovalTests(ApiFixture api)
{
    [Fact]
    public async Task A_book_whose_folder_is_gone_is_removed()
    {
        // One folder still on disk (its empty file won't probe, so it adds no book, but
        // the walk sees the folder), and two stored books whose folders are gone: a
        // plain one and a split one ("<directory>#<album>").
        var root = Directory.CreateTempSubdirectory("audiobook-removal").FullName;
        Directory.CreateDirectory(Path.Combine(root, "Present"));
        await File.WriteAllBytesAsync(Path.Combine(root, "Present", "a.mp3"), []);

        var libraryId = await LibraryAsync(root);
        var gone = await api.CreateBookAsync(60, libraryId: libraryId);
        var goneSplit = await api.CreateBookAsync(60, libraryId: libraryId);
        var kept = await api.CreateBookAsync(60, libraryId: libraryId);
        await SetPathAsync(gone, "Gone");
        await SetPathAsync(goneSplit, "Gone Collection#Volume One");
        await SetPathAsync(kept, "Present#Some Album");

        var report = await ScanAsync(libraryId);

        Assert.Equal(2, report.BooksRemoved);
        await using var db = api.NewDbContext();
        Assert.Equal([kept], await db.Books.Where(b => b.LibraryId == libraryId).Select(b => b.Id).ToListAsync());
    }

    [Fact]
    public async Task A_scan_that_finds_no_folders_removes_nothing()
    {
        // As an unmounted drive would look: the root is there, but empty.
        var root = Directory.CreateTempSubdirectory("audiobook-unmounted").FullName;
        var libraryId = await LibraryAsync(root);
        var book = await api.CreateBookAsync(60, libraryId: libraryId);

        var report = await ScanAsync(libraryId);

        Assert.Equal(0, report.BooksRemoved);
        await using var db = api.NewDbContext();
        Assert.True(await db.Books.AnyAsync(b => b.Id == book));
    }

    private async Task<Guid> LibraryAsync(string root)
    {
        await using var db = api.NewDbContext();
        var library = new Library
        {
            Id = Guid.NewGuid(), Name = "Removal " + Guid.NewGuid().ToString("N")[..6],
            RootPath = root, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Libraries.Add(library);
        await db.SaveChangesAsync();
        return library.Id;
    }

    private async Task SetPathAsync(Guid bookId, string relativePath)
    {
        await using var db = api.NewDbContext();
        await db.Books.Where(b => b.Id == bookId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.RelativePath, relativePath));
    }

    private async Task<ScanReport> ScanAsync(Guid libraryId)
    {
        using var scope = api.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ILibraryScanService>().ScanAsync(libraryId);
    }
}
