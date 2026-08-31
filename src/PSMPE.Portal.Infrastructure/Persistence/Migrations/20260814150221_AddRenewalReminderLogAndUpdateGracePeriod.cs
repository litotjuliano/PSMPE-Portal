using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PSMPE.Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRenewalReminderLogAndUpdateGracePeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RenewalReminderLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReminderType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ForRenewalDueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenewalReminderLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RenewalReminderLogs_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalReminderLogs_MemberId_ReminderType_ForRenewalDueDate",
                table: "RenewalReminderLogs",
                columns: new[] { "MemberId", "ReminderType", "ForRenewalDueDate" },
                unique: true);

            // The existing MembershipGracePeriodDays row still says "30" for every environment
            // seeded before this release - SystemConfigSeeder only fills *missing* keys, it never
            // updates one that already exists, so the new 7-day policy needs an explicit data fix.
            // Safe unconditionally: there is no admin-facing editor for this key anywhere in the
            // codebase, so there is nothing to clobber.
            migrationBuilder.Sql(
                """UPDATE "SystemConfigs" SET "Value" = '7' WHERE "Key" = 'MembershipGracePeriodDays';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """UPDATE "SystemConfigs" SET "Value" = '30' WHERE "Key" = 'MembershipGracePeriodDays';""");

            migrationBuilder.DropTable(
                name: "RenewalReminderLogs");
        }
    }
}
