using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class Ticket23_AddOperationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "causation_id",
                schema: "identity",
                table: "invite_acceptance_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                schema: "identity",
                table: "invite_acceptance_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "message_id",
                schema: "identity",
                table: "invite_acceptance_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "causation_id",
                schema: "identity",
                table: "account_disablements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                schema: "identity",
                table: "account_disablements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "message_id",
                schema: "identity",
                table: "account_disablements",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "causation_id",
                schema: "identity",
                table: "invite_acceptance_operations");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "identity",
                table: "invite_acceptance_operations");

            migrationBuilder.DropColumn(
                name: "message_id",
                schema: "identity",
                table: "invite_acceptance_operations");

            migrationBuilder.DropColumn(
                name: "causation_id",
                schema: "identity",
                table: "account_disablements");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "identity",
                table: "account_disablements");

            migrationBuilder.DropColumn(
                name: "message_id",
                schema: "identity",
                table: "account_disablements");
        }
    }
}
