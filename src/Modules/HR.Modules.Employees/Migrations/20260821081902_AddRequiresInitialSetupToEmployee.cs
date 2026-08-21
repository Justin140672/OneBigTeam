using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddRequiresInitialSetupToEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "initial_setup_completed_at",
                schema: "employees",
                table: "employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_initial_setup",
                schema: "employees",
                table: "employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "initial_setup_completed_at",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "requires_initial_setup",
                schema: "employees",
                table: "employees");
        }
    }
}
