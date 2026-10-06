using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class ReopenLegacyFailedSignUpOperationSweeps : Migration
    {
        internal const string ReopenSweepsSql = @"
            UPDATE identity.signup_operations
            SET swept_at = NULL
            WHERE status = 'failed'
              AND swept_at IS NOT NULL;
        ";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ReopenSweepsSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
