using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Leave.Migrations
{
    /// <inheritdoc />
    public partial class AddIsActiveToEmployeeLeavePolicyAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deactivated_at",
                schema: "leave",
                table: "employee_leave_policy_assignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "leave",
                table: "employee_leave_policy_assignments",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_leave_policy_assignments_company_id_is_active",
                schema: "leave",
                table: "employee_leave_policy_assignments",
                columns: new[] { "company_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_employee_leave_policy_assignments_company_id_is_active",
                schema: "leave",
                table: "employee_leave_policy_assignments");

            migrationBuilder.DropColumn(
                name: "deactivated_at",
                schema: "leave",
                table: "employee_leave_policy_assignments");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "leave",
                table: "employee_leave_policy_assignments");
        }
    }
}
