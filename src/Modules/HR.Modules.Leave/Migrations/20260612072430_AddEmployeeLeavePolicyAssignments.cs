using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Leave.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeLeavePolicyAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_leave_policy_assignments",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_leave_policy_assignments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_leave_policy_assignments_company_id_employee_id",
                schema: "leave",
                table: "employee_leave_policy_assignments",
                columns: new[] { "company_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_leave_policy_assignments_company_id_leave_policy_id",
                schema: "leave",
                table: "employee_leave_policy_assignments",
                columns: new[] { "company_id", "leave_policy_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_leave_policy_assignments",
                schema: "leave");
        }
    }
}
