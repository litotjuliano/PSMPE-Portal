using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PSMPE.Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactAndProfessionalInformationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessAddress",
                table: "Members",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmploymentStatus",
                table: "Members",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacebookUrl",
                table: "Members",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HousePhone",
                table: "Members",
                type: "character varying(32)",
                maxLength: 32,
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
                name: "Position",
                table: "Members",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Skills",
                table: "Members",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Specialization",
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

            migrationBuilder.AddColumn<int>(
                name: "YearsOfPractice",
                table: "Members",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MemberCertificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberCertificates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemberCertificates_UserId",
                table: "MemberCertificates",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemberCertificates");

            migrationBuilder.DropColumn(
                name: "BusinessAddress",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "EmploymentStatus",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "FacebookUrl",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "HousePhone",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "InstagramUrl",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "LinkedInUrl",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "Skills",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "Specialization",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "Website",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "XUrl",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "YearsOfPractice",
                table: "Members");
        }
    }
}
