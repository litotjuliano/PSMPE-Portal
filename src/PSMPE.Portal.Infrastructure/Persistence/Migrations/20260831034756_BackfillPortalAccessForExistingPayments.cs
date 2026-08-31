using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PSMPE.Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillPortalAccessForExistingPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The AddPortalAccessAndFeePromotions migration defaulted every existing member to
            // HasPortalAccess = false, which is correct for a brand-new column but wrong as history:
            // any member with a Verified membership payment recorded before this feature existed
            // paid under the old all-inclusive price - there was no way for them to have "opted out"
            // of portal access, so treating them as if they had locks them out of the app they were
            // already using. Grant access to exactly those members; anyone whose payment history was
            // created after the feature shipped keeps whatever IncludesPortalAccess they actually chose.
            migrationBuilder.Sql(
                """
                UPDATE "Members"
                SET "HasPortalAccess" = true
                WHERE "HasPortalAccess" = false
                  AND EXISTS (
                      SELECT 1 FROM "Payments" p
                      WHERE p."MemberId" = "Members"."Id"
                        AND p."Status" = 'Verified'
                        AND p."Kind" IN ('NewMembership', 'Renewal')
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately not reversed: this is a one-time historical correction, not a toggle.
            // Rolling it back would strip portal access from members who never lost the right to it,
            // and there is no way to distinguish "backfilled by this migration" from "later earned
            // access legitimately through a normal payment" after the fact.
        }
    }
}
