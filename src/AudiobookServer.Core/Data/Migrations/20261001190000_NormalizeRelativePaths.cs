using AudiobookServer.Core.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookServer.Core.Data.Migrations
{
    /// <summary>
    /// Data-only migration, written by hand (no model change, so no designer file and
    /// no snapshot change). The scanner now stores relative paths with '/' on every OS
    /// (LibraryPaths). Rows written by a scan on Windows hold '\', and would otherwise
    /// be treated as new books on the next scan, getting new IDs and losing positions.
    ///
    /// Only libraries whose root is a Windows drive path are touched. On Linux '\' is a
    /// legal filename character, so a blanket replace could corrupt a real path.
    /// </summary>
    [DbContext(typeof(AudiobookDbContext))]
    [Migration("20261001190000_NormalizeRelativePaths")]
    public partial class NormalizeRelativePaths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // standard_conforming_strings is on by default, so '\' is one literal backslash.
            // In the regex, [\\/] is a bracket expression matching '\' or '/'.
            migrationBuilder.Sql("""
                UPDATE "Books"
                SET "RelativePath" = replace("RelativePath", '\', '/')
                WHERE "LibraryId" IN (
                    SELECT "Id" FROM "Libraries" WHERE "RootPath" ~ '^[A-Za-z]:[\\/]');
                """);

            migrationBuilder.Sql("""
                UPDATE "AudioFiles"
                SET "RelativePath" = replace("RelativePath", '\', '/')
                WHERE "BookId" IN (
                    SELECT b."Id"
                    FROM "Books" b
                    JOIN "Libraries" l ON l."Id" = b."LibraryId"
                    WHERE l."RootPath" ~ '^[A-Za-z]:[\\/]');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. '/' resolves correctly on Windows too, so there is
            // nothing to undo, and the original separators aren't recorded.
        }
    }
}
