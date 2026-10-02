using AudiobookServer.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AudiobookServer.Core.Data;

/// <summary>
/// IdentityUserContext rather than IdentityDbContext: users, claims, logins and tokens,
/// but no role tables. A single-user server has nothing to put in them.
/// </summary>
public class AudiobookDbContext(DbContextOptions<AudiobookDbContext> options)
    : IdentityUserContext<User, Guid>(options)
{
    public DbSet<Library> Libraries => Set<Library>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<AudioFile> AudioFiles => Set<AudioFile>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<PlaybackPosition> PlaybackPositions => Set<PlaybackPosition>();
    public DbSet<LibraryGrant> LibraryGrants => Set<LibraryGrant>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Identity's own mapping first (keys, the normalized-name index, lengths), then
        // table names that match the rest of the schema instead of AspNetUsers etc.
        base.OnModelCreating(b);
        b.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        b.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        b.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");

        b.Entity<Library>(e =>
        {
            e.HasIndex(x => x.RootPath).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.RootPath).HasMaxLength(1000);
            e.Property(x => x.Credit).HasMaxLength(200);
        });

        b.Entity<Book>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.Subtitle).HasMaxLength(500);
            e.Property(x => x.Author).HasMaxLength(300);
            e.Property(x => x.Narrator).HasMaxLength(300);
            e.Property(x => x.Isbn).HasMaxLength(20);
            e.Property(x => x.RelativePath).HasMaxLength(1000);
            e.Property(x => x.CoverPath).HasMaxLength(1000);

            // A book is identified by where it lives, so rescans update rather than duplicate.
            e.HasIndex(x => new { x.LibraryId, x.RelativePath }).IsUnique();
            e.HasIndex(x => x.Author);

            e.HasOne(x => x.Library)
             .WithMany(x => x.Books)
             .HasForeignKey(x => x.LibraryId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AudioFile>(e =>
        {
            e.Property(x => x.RelativePath).HasMaxLength(1000);
            e.Property(x => x.MimeType).HasMaxLength(100);
            e.Property(x => x.Codec).HasMaxLength(50);

            e.HasIndex(x => new { x.BookId, x.Sequence }).IsUnique();

            e.HasOne(x => x.Book)
             .WithMany(x => x.Files)
             .HasForeignKey(x => x.BookId)
             .OnDelete(DeleteBehavior.Cascade);

            e.Ignore(x => x.EndOffsetSeconds);
        });

        b.Entity<Chapter>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(500);

            e.HasIndex(x => new { x.BookId, x.Sequence }).IsUnique();
            e.HasIndex(x => new { x.BookId, x.StartOffsetSeconds });

            e.HasOne(x => x.Book)
             .WithMany(x => x.Chapters)
             .HasForeignKey(x => x.BookId)
             .OnDelete(DeleteBehavior.Cascade);

            e.Ignore(x => x.DurationSeconds);
        });

        b.Entity<User>(e => e.ToTable("Users"));

        b.Entity<LibraryGrant>(e =>
        {
            // One row per account per library; the key doubles as the lookup index
            // VisibleBooks uses (UserId first).
            e.HasKey(x => new { x.UserId, x.LibraryId });

            e.HasOne(x => x.User)
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Library)
             .WithMany()
             .HasForeignKey(x => x.LibraryId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Device>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Platform).HasMaxLength(100);

            e.HasOne(x => x.User)
             .WithMany(x => x.Devices)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PlaybackPosition>(e =>
        {
            // One position per user per book. Devices report into the same row.
            e.HasIndex(x => new { x.UserId, x.BookId }).IsUnique();

            e.HasOne(x => x.User)
             .WithMany(x => x.Positions)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Book)
             .WithMany()
             .HasForeignKey(x => x.BookId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Device)
             .WithMany()
             .HasForeignKey(x => x.DeviceId)
             .OnDelete(DeleteBehavior.SetNull);
        });
    }
}