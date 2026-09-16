using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountDisablementClaimLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "claimed_by",
                schema: "identity",
                table: "account_disablements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_terminally_failed",
                schema: "identity",
                table: "account_disablements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_retried_at",
                schema: "identity",
                table: "account_disablements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "last_retried_by_actor_id",
                schema: "identity",
                table: "account_disablements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_retry_reason",
                schema: "identity",
                table: "account_disablements",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_expires_at",
                schema: "identity",
                table: "account_disablements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "identity",
                table: "account_disablements",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "claimed_by",
                schema: "identity",
                table: "account_disablements");

            migrationBuilder.DropColumn(
                name: "is_terminally_failed",
                schema: "identity",
                table: "account_disablements");

            migrationBuilder.DropColumn(
                name: "last_retried_at",
                schema: "identity",
                table: "account_disablements");

            migrationBuilder.DropColumn(
                name: "last_retried_by_actor_id",
                schema: "identity",
                table: "account_disablements");

            migrationBuilder.DropColumn(
                name: "last_retry_reason",
                schema: "identity",
                table: "account_disablements");

            migrationBuilder.DropColumn(
                name: "lease_expires_at",
                schema: "identity",
                table: "account_disablements");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "identity",
                table: "account_disablements");
        }
    }
}
