using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PSMPE.Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defaults every existing event to Published - they were already fully visible to
            // members before this column existed, and this migration must not retroactively hide
            // them (the same backfill lesson as this session's AddPortalAccessAndFeePromotions/
            // BackfillPortalAccessForExistingPayments migrations). New events going forward are
            // always set explicitly by EventService.CreateAsync, so this default only matters here.
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Published");

            migrationBuilder.CreateIndex(
                name: "IX_Events_Status",
                table: "Events",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Events_Status",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Events");
        }
    }
}
