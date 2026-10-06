using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class SignUpOperationNoPasswordFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "request_fingerprint",
                schema: "identity",
                table: "signup_operations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            // Existing fingerprints are unkeyed SHA-256 hashes over the full request including the
            // password, so none may survive. Terminal rows that can no longer be replayed (released
            // key, or past the 7 day retention window) are purged; surviving rows keep only a
            // non-secret marker that the handler accepts when the email still matches.
            migrationBuilder.Sql(@"
                DELETE FROM identity.signup_operations
                WHERE status <> 'in_progress'
                  AND (idempotency_key IS NULL
                       OR COALESCE(completed_at, updated_at) < now() - interval '7 days');

                UPDATE identity.signup_operations
                SET request_fingerprint = CASE WHEN idempotency_key IS NULL THEN NULL ELSE 'legacy' END,
                    last_error = NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "request_fingerprint",
                schema: "identity",
                table: "signup_operations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);
        }
    }
}
