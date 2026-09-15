using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Leave.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoricalLeaveDeactivationRepairProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "historical_leave_deactivation_repair_progress",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_processed_finalisation_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_processed_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_complete = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    total_repaired = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_historical_leave_deactivation_repair_progress", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "historical_leave_deactivation_repair_progress",
                schema: "leave");
        }
    }
}
