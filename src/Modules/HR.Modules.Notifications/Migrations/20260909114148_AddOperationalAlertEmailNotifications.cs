using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalAlertEmailNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "affected_item_count",
                schema: "notifications",
                table: "administrative_alerts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "operational_alert_email_deliveries",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operational_alert_email_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "FK_operational_alert_email_deliveries_administrative_alerts_al~",
                        column: x => x.alert_id,
                        principalSchema: "notifications",
                        principalTable: "administrative_alerts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_operational_alert_email_deliveries_alert_id",
                schema: "notifications",
                table: "operational_alert_email_deliveries",
                column: "alert_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operational_alert_email_deliveries",
                schema: "notifications");

            migrationBuilder.DropColumn(
                name: "affected_item_count",
                schema: "notifications",
                table: "administrative_alerts");
        }
    }
}
