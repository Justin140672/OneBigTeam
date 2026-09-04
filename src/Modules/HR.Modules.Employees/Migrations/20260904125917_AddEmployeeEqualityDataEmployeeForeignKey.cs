using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <summary>
    /// Ticket 8 — Equality Data Retention and Employee Deletion. Adds the missing FK from
    /// employees.employee_equality_data.employee_id to employees.employees.id with
    /// ON DELETE CASCADE so a special-category equality-monitoring record can never exist as an
    /// identifiable orphan and is destroyed automatically the moment its employee row is physically
    /// deleted (per-store customer deletion / full-tenant schema drop). Employees are only
    /// soft-deleted in normal operation, so this cascade never fires during offboarding/leaving.
    /// The table was introduced on 2026-09-04 and is not yet in production, so adding the constraint
    /// is a forward-only, low-risk change; Down() cleanly reverses it.
    /// </summary>
    public partial class AddEmployeeEqualityDataEmployeeForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_employee_equality_data_employee_id",
                schema: "employees",
                table: "employee_equality_data",
                column: "employee_id");

            migrationBuilder.AddForeignKey(
                name: "FK_employee_equality_data_employees_employee_id",
                schema: "employees",
                table: "employee_equality_data",
                column: "employee_id",
                principalSchema: "employees",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_employee_equality_data_employees_employee_id",
                schema: "employees",
                table: "employee_equality_data");

            migrationBuilder.DropIndex(
                name: "IX_employee_equality_data_employee_id",
                schema: "employees",
                table: "employee_equality_data");
        }
    }
}
