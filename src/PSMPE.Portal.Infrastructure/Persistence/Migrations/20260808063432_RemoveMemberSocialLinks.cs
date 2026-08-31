using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PSMPE.Portal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Drops the five social/website link columns. The scaffolder flags this as possible data loss;
    /// it was checked against the live data first and every one of these columns was NULL for every
    /// member, so nothing is actually destroyed. Down() restores the columns but not their contents.
    /// </summary>
    public partial class RemoveMemberSocialLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FacebookUrl",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "InstagramUrl",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "LinkedInUrl",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "Website",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "XUrl",
                table: "Members");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FacebookUrl",
                table: "Members",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramUrl",
                table: "Members",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedInUrl",
                table: "Members",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "Members",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "XUrl",
                table: "Members",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }
    }
}
