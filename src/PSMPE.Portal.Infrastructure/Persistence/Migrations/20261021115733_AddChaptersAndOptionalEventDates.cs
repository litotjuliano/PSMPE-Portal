using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PSMPE.Portal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Turns the free-text Member.Chapter / Event.Chapter columns into a real Chapters table with
    /// ChapterId foreign keys, and makes Event.StartsAt/EndsAt nullable (Drafts only - see
    /// Event.StartsAt). Hand-ordered instead of the scaffolded drop-then-add, which would have
    /// discarded every member's and event's chapter: rows are created and backfilled first, and the
    /// old columns are only dropped once their values live in ChapterId.
    /// </summary>
    public partial class AddChaptersAndOptionalEventDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Chapters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chapters", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_Name",
                table: "Chapters",
                column: "Name",
                unique: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChapterId",
                table: "Members",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChapterId",
                table: "Events",
                type: "uuid",
                nullable: true);

            // The six chapters that used to be hardcoded (Domain/Enums/Chapters.cs), in their old order.
            migrationBuilder.Sql(@"
                INSERT INTO ""Chapters"" (""Id"", ""Name"", ""SortOrder"", ""IsActive"", ""CreatedAt"")
                VALUES
                    (gen_random_uuid(), 'NCR', 1, TRUE, now()),
                    (gen_random_uuid(), 'Cebu', 2, TRUE, now()),
                    (gen_random_uuid(), 'Davao', 3, TRUE, now()),
                    (gen_random_uuid(), 'Baguio', 4, TRUE, now()),
                    (gen_random_uuid(), 'Cavite', 5, TRUE, now()),
                    (gen_random_uuid(), 'Quezon City Chapter', 6, TRUE, now());");

            // Members never had a server-side whitelist, so the live data may hold values outside
            // that list. Keep each distinct one (case-insensitively) as an inactive chapter instead of
            // silently orphaning those members: they stay assigned and visible, but admins can tidy up
            // and the value doesn't show in new-selection dropdowns.
            migrationBuilder.Sql(@"
                INSERT INTO ""Chapters"" (""Id"", ""Name"", ""SortOrder"", ""IsActive"", ""CreatedAt"")
                SELECT gen_random_uuid(), s.name, 100 + row_number() OVER (ORDER BY s.name), FALSE, now()
                FROM (
                    SELECT DISTINCT ON (lower(name)) name
                    FROM (
                        SELECT btrim(""Chapter"") AS name FROM ""Members""
                        UNION ALL
                        SELECT btrim(""Chapter"") FROM ""Events"" WHERE ""Chapter"" IS NOT NULL
                    ) raw
                    WHERE name <> ''
                    ORDER BY lower(name), name
                ) s
                WHERE NOT EXISTS (SELECT 1 FROM ""Chapters"" c WHERE lower(c.""Name"") = lower(s.name));");

            migrationBuilder.Sql(@"
                UPDATE ""Members"" m SET ""ChapterId"" = c.""Id""
                FROM ""Chapters"" c
                WHERE lower(btrim(m.""Chapter"")) = lower(c.""Name"");");

            migrationBuilder.Sql(@"
                UPDATE ""Events"" e SET ""ChapterId"" = c.""Id""
                FROM ""Chapters"" c
                WHERE e.""Chapter"" IS NOT NULL AND lower(btrim(e.""Chapter"")) = lower(c.""Name"");");

            migrationBuilder.CreateIndex(
                name: "IX_Members_ChapterId",
                table: "Members",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_ChapterId",
                table: "Events",
                column: "ChapterId");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Chapters_ChapterId",
                table: "Events",
                column: "ChapterId",
                principalTable: "Chapters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Members_Chapters_ChapterId",
                table: "Members",
                column: "ChapterId",
                principalTable: "Chapters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropColumn(
                name: "Chapter",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "Chapter",
                table: "Events");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "StartsAt",
                table: "Events",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EndsAt",
                table: "Events",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Chapter",
                table: "Members",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Chapter",
                table: "Events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""Members"" m SET ""Chapter"" = c.""Name""
                FROM ""Chapters"" c WHERE m.""ChapterId"" = c.""Id"";");

            migrationBuilder.Sql(@"
                UPDATE ""Events"" e SET ""Chapter"" = c.""Name""
                FROM ""Chapters"" c WHERE e.""ChapterId"" = c.""Id"";");

            // The old columns were NOT NULL; an undated Draft has no real value, so fall back to its
            // creation time rather than fail the rollback.
            migrationBuilder.Sql(@"
                UPDATE ""Events"" SET ""StartsAt"" = COALESCE(""StartsAt"", ""CreatedAt""),
                                      ""EndsAt"" = COALESCE(""EndsAt"", COALESCE(""StartsAt"", ""CreatedAt""));");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "StartsAt",
                table: "Events",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EndsAt",
                table: "Events",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "FK_Events_Chapters_ChapterId",
                table: "Events");

            migrationBuilder.DropForeignKey(
                name: "FK_Members_Chapters_ChapterId",
                table: "Members");

            migrationBuilder.DropIndex(
                name: "IX_Members_ChapterId",
                table: "Members");

            migrationBuilder.DropIndex(
                name: "IX_Events_ChapterId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "ChapterId",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "ChapterId",
                table: "Events");

            migrationBuilder.DropTable(
                name: "Chapters");
        }
    }
}
