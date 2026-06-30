using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeEmploymentTypeIdFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "employment_type",
                schema: "employees",
                table: "employees");

            migrationBuilder.AddColumn<Guid>(
                name: "employment_type_id",
                schema: "employees",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_employees_employment_type_id",
                schema: "employees",
                table: "employees",
                column: "employment_type_id");

            migrationBuilder.AddForeignKey(
                name: "FK_employees_employment_types_employment_type_id",
                schema: "employees",
                table: "employees",
                column: "employment_type_id",
                principalSchema: "employees",
                principalTable: "employment_types",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_employees_employment_types_employment_type_id",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "IX_employees_employment_type_id",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "employment_type_id",
                schema: "employees",
                table: "employees");

            migrationBuilder.AddColumn<string>(
                name: "employment_type",
                schema: "employees",
                table: "employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
