using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookServer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDurationProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationSource",
                table: "AudioFiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "HeaderDurationSeconds",
                table: "AudioFiles",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            // Every existing row was built from ffprobe's header duration, so the
            // header value is the stored duration and the defaults above (0 = Header)
            // are already correct for DurationSource. A forced rescan then replaces
            // the mp3 rows with packet-counted ones.
            migrationBuilder.Sql(
                "UPDATE \"AudioFiles\" SET \"HeaderDurationSeconds\" = \"DurationSeconds\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DurationSource",
                table: "AudioFiles");

            migrationBuilder.DropColumn(
                name: "HeaderDurationSeconds",
                table: "AudioFiles");
        }
    }
}
