using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Leave.Migrations
{
    /// <inheritdoc />
    public partial class AddLeavePolicyDeactivationOnDeparture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leave_policy_deactivations_on_departure",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leave_policy_deactivations_on_departure", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_deactivations_on_departure_company_employee",
                schema: "leave",
                table: "leave_policy_deactivations_on_departure",
                columns: new[] { "company_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_leave_policy_deactivations_on_departure_company_id",
                schema: "leave",
                table: "leave_policy_deactivations_on_departure",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_leave_policy_deactivations_on_departure_status_requested_at",
                schema: "leave",
                table: "leave_policy_deactivations_on_departure",
                columns: new[] { "status", "requested_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leave_policy_deactivations_on_departure",
                schema: "leave");
        }
    }
}
