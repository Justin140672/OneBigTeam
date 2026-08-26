using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddRetryTrackingToOutboxMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                schema: "companies",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "error_message",
                schema: "companies",
                table: "outbox_messages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "failed_at",
                schema: "companies",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_attempt_at",
                schema: "companies",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_company_id_event_type_in_flight",
                schema: "companies",
                table: "outbox_messages",
                columns: new[] { "company_id", "event_type" },
                unique: true,
                filter: "status IN ('pending', 'processing')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_company_id_event_type_in_flight",
                schema: "companies",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "attempt_count",
                schema: "companies",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "error_message",
                schema: "companies",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "failed_at",
                schema: "companies",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "last_attempt_at",
                schema: "companies",
                table: "outbox_messages");
        }
    }
}
