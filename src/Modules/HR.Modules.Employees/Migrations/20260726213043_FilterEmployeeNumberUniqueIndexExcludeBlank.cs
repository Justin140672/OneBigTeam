using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class FilterEmployeeNumberUniqueIndexExcludeBlank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_employees_company_id_employee_number",
                schema: "employees",
                table: "employees");

            migrationBuilder.CreateIndex(
                name: "IX_employees_company_id_employee_number",
                schema: "employees",
                table: "employees",
                columns: new[] { "company_id", "employee_number" },
                unique: true,
                filter: "employee_number <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_employees_company_id_employee_number",
                schema: "employees",
                table: "employees");

            migrationBuilder.CreateIndex(
                name: "IX_employees_company_id_employee_number",
                schema: "employees",
                table: "employees",
                columns: new[] { "company_id", "employee_number" },
                unique: true);
        }
    }
}
