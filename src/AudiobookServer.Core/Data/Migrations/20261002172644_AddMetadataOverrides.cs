using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookServer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthorOverride",
                table: "Books",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TitleOverride",
                table: "Books",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthorOverride",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "TitleOverride",
                table: "Books");
        }
    }
}
