using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Sickness.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attendance_alerts",
                schema: "sickness",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    evidence_period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    evidence_period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    occurrence_count = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_alerts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_alerts_company_id",
                schema: "sickness",
                table: "attendance_alerts",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_alerts_company_id_employee_id_rule_evidence_peri~",
                schema: "sickness",
                table: "attendance_alerts",
                columns: new[] { "company_id", "employee_id", "rule", "evidence_period_start", "evidence_period_end" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_alerts",
                schema: "sickness");
        }
    }
}
