using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookServer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBlurbSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DescriptionSource",
                table: "Books",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DescriptionSource",
                table: "Books");
        }
    }
}
