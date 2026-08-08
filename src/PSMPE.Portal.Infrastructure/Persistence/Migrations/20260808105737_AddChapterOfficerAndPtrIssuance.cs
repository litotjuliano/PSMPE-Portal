using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PSMPE.Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChapterOfficerAndPtrIssuance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChapterPosition",
                table: "Members",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChapterYear",
                table: "Members",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PtrDateIssued",
                table: "Members",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PtrPlaceIssued",
                table: "Members",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChapterPosition",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "ChapterYear",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "PtrDateIssued",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "PtrPlaceIssued",
                table: "Members");
        }
    }
}
