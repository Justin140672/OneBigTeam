using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddIsEmailConfirmedToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill every pre-existing account (seeded personas, invited users) as already
            // confirmed — only self-service SignUp inserts explicitly pass false going forward.
            migrationBuilder.AddColumn<bool>(
                name: "is_email_confirmed",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_email_confirmed",
                schema: "identity",
                table: "users");
        }
    }
}
