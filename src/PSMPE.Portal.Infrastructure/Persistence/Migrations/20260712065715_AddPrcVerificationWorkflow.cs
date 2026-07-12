using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PSMPE.Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrcVerificationWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PendingPrcLicenseNo",
                table: "Members",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrcVerificationRejectedReason",
                table: "Members",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PrcVerificationHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    OldValue = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DocumentStorageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrcVerificationHistories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrcVerificationHistories_MemberId",
                table: "PrcVerificationHistories",
                column: "MemberId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrcVerificationHistories");

            migrationBuilder.DropColumn(
                name: "PendingPrcLicenseNo",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "PrcVerificationRejectedReason",
                table: "Members");
        }
    }
}
