using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalDetailsToEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "date_of_birth",
                schema: "employees",
                table: "employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gender",
                schema: "employees",
                table: "employees",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nationality",
                schema: "employees",
                table: "employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "preferred_name",
                schema: "employees",
                table: "employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "date_of_birth",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "gender",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "nationality",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "preferred_name",
                schema: "employees",
                table: "employees");
        }
    }
}
