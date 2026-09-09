using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgStructureConcurrencyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "employees",
                table: "onboarding_templates",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "employees",
                table: "locations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "employees",
                table: "location_types",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "employees",
                table: "employment_types",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "employees",
                table: "departments",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                schema: "employees",
                table: "onboarding_templates");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "employees",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "employees",
                table: "location_types");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "employees",
                table: "employment_types");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "employees",
                table: "departments");
        }
    }
}
