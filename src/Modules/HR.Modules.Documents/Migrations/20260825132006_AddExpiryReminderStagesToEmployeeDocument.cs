using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddExpiryReminderStagesToEmployeeDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expiry_reminder_30_sent_at",
                schema: "documents",
                table: "employee_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expiry_reminder_7_sent_at",
                schema: "documents",
                table: "employee_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expiry_reminder_90_sent_at",
                schema: "documents",
                table: "employee_documents",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expiry_reminder_30_sent_at",
                schema: "documents",
                table: "employee_documents");

            migrationBuilder.DropColumn(
                name: "expiry_reminder_7_sent_at",
                schema: "documents",
                table: "employee_documents");

            migrationBuilder.DropColumn(
                name: "expiry_reminder_90_sent_at",
                schema: "documents",
                table: "employee_documents");
        }
    }
}
