using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddSignUpOperationLeaseFencing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "compensation_code",
                schema: "identity",
                table: "signup_operations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "compensation_release_key",
                schema: "identity",
                table: "signup_operations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "lease_token",
                schema: "identity",
                table: "signup_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "swept_at",
                schema: "identity",
                table: "signup_operations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE identity.signup_operations
                SET lease_token = gen_random_uuid()
                WHERE status = 'in_progress' AND lease_expires_at IS NOT NULL;

                UPDATE identity.signup_operations
                SET swept_at = now()
                WHERE status = 'failed';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "compensation_code",
                schema: "identity",
                table: "signup_operations");

            migrationBuilder.DropColumn(
                name: "compensation_release_key",
                schema: "identity",
                table: "signup_operations");

            migrationBuilder.DropColumn(
                name: "lease_token",
                schema: "identity",
                table: "signup_operations");

            migrationBuilder.DropColumn(
                name: "swept_at",
                schema: "identity",
                table: "signup_operations");
        }
    }
}
