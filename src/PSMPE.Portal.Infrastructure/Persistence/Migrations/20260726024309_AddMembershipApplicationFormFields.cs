using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PSMPE.Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipApplicationFormFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "Members");

            migrationBuilder.AddColumn<string>(
                name: "Barangay",
                table: "Members",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CityMunicipality",
                table: "Members",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CourseYearGraduated",
                table: "Members",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EducationLevel",
                table: "Members",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HouseNo",
                table: "Members",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MailingBarangay",
                table: "Members",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MailingCityMunicipality",
                table: "Members",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MailingHouseNo",
                table: "Members",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MailingProvince",
                table: "Members",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MailingStreet",
                table: "Members",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MailingZipCode",
                table: "Members",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PendingPrcRegistrationDate",
                table: "Members",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PendingPrcValidUntilDate",
                table: "Members",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PrcRegistrationDate",
                table: "Members",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PrcValidUntilDate",
                table: "Members",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Province",
                table: "Members",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchoolName",
                table: "Members",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecifiedProfession",
                table: "Members",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "Members",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZipCode",
                table: "Members",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Barangay",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "CityMunicipality",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "CourseYearGraduated",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "EducationLevel",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "HouseNo",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "MailingBarangay",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "MailingCityMunicipality",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "MailingHouseNo",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "MailingProvince",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "MailingStreet",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "MailingZipCode",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "PendingPrcRegistrationDate",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "PendingPrcValidUntilDate",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "PrcRegistrationDate",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "PrcValidUntilDate",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "Province",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "SchoolName",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "SpecifiedProfession",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "ZipCode",
                table: "Members");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Members",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }
    }
}
