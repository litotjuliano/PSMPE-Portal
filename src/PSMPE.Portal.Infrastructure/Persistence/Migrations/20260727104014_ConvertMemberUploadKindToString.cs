using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PSMPE.Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConvertMemberUploadKindToString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A plain type-cast (int::varchar) would turn 0/1/2... into the literal strings
            // "0"/"1"/"2" - which Enum.Parse would still resolve purely by ordinal, leaving
            // existing rows exactly as fragile to a future UploadKind reorder as they are today.
            // This explicit CASE maps each row's *current* ordinal to today's real enum member
            // name, so from this point on every row is self-describing and immune to reordering.
            migrationBuilder.Sql(
                """
                ALTER TABLE "MemberUploads" ALTER COLUMN "Kind" TYPE character varying(32)
                USING (CASE "Kind"
                    WHEN 0 THEN 'Photo'
                    WHEN 1 THEN 'PrcId'
                    WHEN 2 THEN 'ValidGovernmentId'
                    WHEN 3 THEN 'Signature'
                    WHEN 4 THEN 'ProofOfPayment'
                    WHEN 5 THEN 'Receipt'
                END);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "MemberUploads" ALTER COLUMN "Kind" TYPE integer
                USING (CASE "Kind"
                    WHEN 'Photo' THEN 0
                    WHEN 'PrcId' THEN 1
                    WHEN 'ValidGovernmentId' THEN 2
                    WHEN 'Signature' THEN 3
                    WHEN 'ProofOfPayment' THEN 4
                    WHEN 'Receipt' THEN 5
                END);
                """);
        }
    }
}
