using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddEmploymentDetailsToEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "employee_number",
                schema: "employees",
                table: "employees",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "employment_type",
                schema: "employees",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "continuous_service_date",
                schema: "employees",
                table: "employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "probation_end_date",
                schema: "employees",
                table: "employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "leaving_date",
                schema: "employees",
                table: "employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                schema: "employees",
                table: "employees",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "employee_number",       schema: "employees", table: "employees");
            migrationBuilder.DropColumn(name: "employment_type",       schema: "employees", table: "employees");
            migrationBuilder.DropColumn(name: "continuous_service_date", schema: "employees", table: "employees");
            migrationBuilder.DropColumn(name: "probation_end_date",    schema: "employees", table: "employees");
            migrationBuilder.DropColumn(name: "leaving_date",          schema: "employees", table: "employees");
            migrationBuilder.DropColumn(name: "notes",                 schema: "employees", table: "employees");
        }
    }
}
