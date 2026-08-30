using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministrativeAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "administrative_alerts",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    severity = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    dedup_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    occurrence_count = table.Column<int>(type: "integer", nullable: false),
                    first_occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    affected_entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    affected_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recommended_action = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    action_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_administrative_alerts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_administrative_alerts_company_id_dedup_key",
                schema: "notifications",
                table: "administrative_alerts",
                columns: new[] { "company_id", "dedup_key" },
                unique: true,
                filter: "status <> 3");

            migrationBuilder.CreateIndex(
                name: "IX_administrative_alerts_company_id_is_read",
                schema: "notifications",
                table: "administrative_alerts",
                columns: new[] { "company_id", "is_read" });

            migrationBuilder.CreateIndex(
                name: "IX_administrative_alerts_company_id_status_severity_last_occur~",
                schema: "notifications",
                table: "administrative_alerts",
                columns: new[] { "company_id", "status", "severity", "last_occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "administrative_alerts",
                schema: "notifications");
        }
    }
}
