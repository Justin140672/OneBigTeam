using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddExpiryNotificationColumnsToEmployeeDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expired_notified_at",
                schema: "documents",
                table: "employee_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expiring_soon_notified_at",
                schema: "documents",
                table: "employee_documents",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expired_notified_at",
                schema: "documents",
                table: "employee_documents");

            migrationBuilder.DropColumn(
                name: "expiring_soon_notified_at",
                schema: "documents",
                table: "employee_documents");
        }
    }
}
