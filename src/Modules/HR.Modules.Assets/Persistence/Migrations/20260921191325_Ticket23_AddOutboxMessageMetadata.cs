using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Assets.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Ticket23_AddOutboxMessageMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "causation_id",
                schema: "assets",
                table: "audit_outbox",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                schema: "assets",
                table: "audit_outbox",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "message_id",
                schema: "assets",
                table: "audit_outbox",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "causation_id",
                schema: "assets",
                table: "audit_outbox");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "assets",
                table: "audit_outbox");

            migrationBuilder.DropColumn(
                name: "message_id",
                schema: "assets",
                table: "audit_outbox");
        }
    }
}
