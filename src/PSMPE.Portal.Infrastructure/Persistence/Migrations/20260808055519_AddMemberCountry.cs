using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PSMPE.Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberCountry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Members",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MailingCountry",
                table: "Members",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Backfill anyone who already has address data on file. Leaving these null would show
            // an unanswered Country on every profile that predates this change, for a value that's
            // true of essentially the whole membership. Rows with no address yet (blank drafts)
            // stay null and get filled by the form's own "Philippines" default on first save.
            migrationBuilder.Sql(
                """
                UPDATE "Members"
                SET "Country" = 'Philippines'
                WHERE "Country" IS NULL AND ("Province" IS NOT NULL OR "CityMunicipality" IS NOT NULL);

                UPDATE "Members"
                SET "MailingCountry" = 'Philippines'
                WHERE "MailingCountry" IS NULL AND ("MailingProvince" IS NOT NULL OR "MailingCityMunicipality" IS NOT NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Country",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "MailingCountry",
                table: "Members");
        }
    }
}
