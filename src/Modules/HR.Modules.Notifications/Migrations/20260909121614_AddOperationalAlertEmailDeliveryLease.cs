using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalAlertEmailDeliveryLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_acquired_at",
                schema: "notifications",
                table: "operational_alert_email_deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_expires_at",
                schema: "notifications",
                table: "operational_alert_email_deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "lease_owner_token",
                schema: "notifications",
                table: "operational_alert_email_deliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_operational_alert_email_deliveries_status",
                schema: "notifications",
                table: "operational_alert_email_deliveries",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_operational_alert_email_deliveries_status",
                schema: "notifications",
                table: "operational_alert_email_deliveries");

            migrationBuilder.DropColumn(
                name: "lease_acquired_at",
                schema: "notifications",
                table: "operational_alert_email_deliveries");

            migrationBuilder.DropColumn(
                name: "lease_expires_at",
                schema: "notifications",
                table: "operational_alert_email_deliveries");

            migrationBuilder.DropColumn(
                name: "lease_owner_token",
                schema: "notifications",
                table: "operational_alert_email_deliveries");
        }
    }
}
