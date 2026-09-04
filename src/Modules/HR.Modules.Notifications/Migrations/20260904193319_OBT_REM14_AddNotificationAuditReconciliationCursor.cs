using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class OBT_REM14_AddNotificationAuditReconciliationCursor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_audit_reconciliation_cursors",
                schema: "notifications",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_scanned_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_scanned_notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_audit_reconciliation_cursors", x => x.company_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_audit_reconciliation_cursors",
                schema: "notifications");
        }
    }
}
