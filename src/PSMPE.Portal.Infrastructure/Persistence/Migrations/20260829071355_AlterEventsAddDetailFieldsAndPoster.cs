using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PSMPE.Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlterEventsAddDetailFieldsAndPoster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Fee",
                table: "Events",
                newName: "FeeOnsite");

            migrationBuilder.AddColumn<string>(
                name: "Venue",
                table: "EventSessions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CpdCodeOnline",
                table: "Events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CpdCodeOnsite",
                table: "Events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FeeOnline",
                table: "Events",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Hours",
                table: "Events",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Objectives",
                table: "Events",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PosterImageStorageKey",
                table: "Events",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Venue",
                table: "EventSessions");

            migrationBuilder.DropColumn(
                name: "CpdCodeOnline",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "CpdCodeOnsite",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "FeeOnline",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Hours",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Objectives",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "PosterImageStorageKey",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Events");

            migrationBuilder.RenameColumn(
                name: "FeeOnsite",
                table: "Events",
                newName: "Fee");
        }
    }
}
