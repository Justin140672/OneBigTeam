using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformAdministratorProvisioningWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_new_identity_provider_account",
                schema: "identity",
                table: "platform_administrators",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "provisioning_completed_at",
                schema: "identity",
                table: "platform_administrators",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "provisioning_correlation_id",
                schema: "identity",
                table: "platform_administrators",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provisioning_failure_reason",
                schema: "identity",
                table: "platform_administrators",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "provisioning_started_at",
                schema: "identity",
                table: "platform_administrators",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provisioning_status",
                schema: "identity",
                table: "platform_administrators",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "active");

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "identity",
                table: "platform_administrators",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ix_platform_administrators_supabase_auth_user_id_unique",
                schema: "identity",
                table: "platform_administrators",
                column: "supabase_auth_user_id",
                unique: true,
                filter: "supabase_auth_user_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_platform_administrators_supabase_auth_user_id_unique",
                schema: "identity",
                table: "platform_administrators");

            migrationBuilder.DropColumn(
                name: "is_new_identity_provider_account",
                schema: "identity",
                table: "platform_administrators");

            migrationBuilder.DropColumn(
                name: "provisioning_completed_at",
                schema: "identity",
                table: "platform_administrators");

            migrationBuilder.DropColumn(
                name: "provisioning_correlation_id",
                schema: "identity",
                table: "platform_administrators");

            migrationBuilder.DropColumn(
                name: "provisioning_failure_reason",
                schema: "identity",
                table: "platform_administrators");

            migrationBuilder.DropColumn(
                name: "provisioning_started_at",
                schema: "identity",
                table: "platform_administrators");

            migrationBuilder.DropColumn(
                name: "provisioning_status",
                schema: "identity",
                table: "platform_administrators");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "identity",
                table: "platform_administrators");
        }
    }
}
