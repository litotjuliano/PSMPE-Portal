using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PSMPE.Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDataPrivacyConsentToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DataPrivacyConsentAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DataPrivacyConsentVersion",
                table: "AspNetUsers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataPrivacyConsentAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DataPrivacyConsentVersion",
                table: "AspNetUsers");
        }
    }
}
