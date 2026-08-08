using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PSMPE.Portal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Swaps the plain unique index on MembershipNo for a case-insensitive one.
    ///
    /// The old btree index compared bytes, so "PSMPE-001" and "psmpe-001" were distinct values and
    /// two members could legitimately hold what everyone would read as the same control number.
    /// Indexing lower("MembershipNo") instead makes the database enforce what
    /// MemberService.MembershipNoExistsAsync checks, which matters because that check can always
    /// lose a race with a concurrent approval.
    ///
    /// EF cannot express a functional index, so both statements are raw SQL and the index is
    /// deliberately absent from MemberConfiguration - see the comment there.
    ///
    /// Verified before writing: no existing row collides case-insensitively, so the index builds
    /// without a data fix.
    /// </summary>
    public partial class MembershipNoCaseInsensitiveUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Members_MembershipNo",
                table: "Members");

            // NULLs are distinct under a unique index in Postgres, so applicants still awaiting a
            // number don't collide with each other - same behaviour the dropped index had.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "IX_Members_MembershipNo_Lower"
                ON "Members" (lower("MembershipNo"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Members_MembershipNo_Lower";""");

            migrationBuilder.CreateIndex(
                name: "IX_Members_MembershipNo",
                table: "Members",
                column: "MembershipNo",
                unique: true);
        }
    }
}
