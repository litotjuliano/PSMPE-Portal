using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PSMPE.Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberPersonalInformationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CivilStatus",
                table: "Members",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MobileNumber",
                table: "Members",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PtrNumber",
                table: "Members",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tin",
                table: "Members",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CivilStatus",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "MobileNumber",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "PtrNumber",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "Tin",
                table: "Members");
        }
    }
}
