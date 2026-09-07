using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookServer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Libraries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RootPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastScanStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastScanCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Libraries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Books",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Subtitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Author = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Narrator = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Isbn = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PublishedYear = table.Column<int>(type: "integer", nullable: true),
                    RelativePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    CoverPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LibraryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastScannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Books", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Books_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Platform = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Devices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AudioFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    StartOffsetSeconds = table.Column<double>(type: "double precision", nullable: false),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Codec = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Bitrate = table.Column<int>(type: "integer", nullable: true),
                    FileModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudioFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AudioFiles_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Chapters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    StartOffsetSeconds = table.Column<double>(type: "double precision", nullable: false),
                    EndOffsetSeconds = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Chapters_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaybackPositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    PositionSeconds = table.Column<double>(type: "double precision", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsFinished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybackPositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaybackPositions_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaybackPositions_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlaybackPositions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AudioFiles_BookId_Sequence",
                table: "AudioFiles",
                columns: new[] { "BookId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Books_Author",
                table: "Books",
                column: "Author");

            migrationBuilder.CreateIndex(
                name: "IX_Books_LibraryId_RelativePath",
                table: "Books",
                columns: new[] { "LibraryId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_BookId_Sequence",
                table: "Chapters",
                columns: new[] { "BookId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_BookId_StartOffsetSeconds",
                table: "Chapters",
                columns: new[] { "BookId", "StartOffsetSeconds" });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_UserId",
                table: "Devices",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Libraries_RootPath",
                table: "Libraries",
                column: "RootPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackPositions_BookId",
                table: "PlaybackPositions",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackPositions_DeviceId",
                table: "PlaybackPositions",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackPositions_UserId_BookId",
                table: "PlaybackPositions",
                columns: new[] { "UserId", "BookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AudioFiles");

            migrationBuilder.DropTable(
                name: "Chapters");

            migrationBuilder.DropTable(
                name: "PlaybackPositions");

            migrationBuilder.DropTable(
                name: "Books");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "Libraries");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
