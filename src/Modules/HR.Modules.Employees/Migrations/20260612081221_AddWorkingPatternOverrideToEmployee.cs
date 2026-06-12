using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkingPatternOverrideToEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "hours_per_day_override",
                schema: "employees",
                table: "employees",
                type: "numeric(4,2)",
                precision: 4,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "working_days_override",
                schema: "employees",
                table: "employees",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hours_per_day_override",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "working_days_override",
                schema: "employees",
                table: "employees");
        }
    }
}
