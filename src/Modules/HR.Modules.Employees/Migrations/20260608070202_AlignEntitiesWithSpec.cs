using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AlignEntitiesWithSpec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hired_on",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "employees",
                table: "employees");

            migrationBuilder.RenameColumn(
                name: "email",
                schema: "employees",
                table: "employees",
                newName: "work_email");

            migrationBuilder.RenameIndex(
                name: "IX_employees_company_id_email",
                schema: "employees",
                table: "employees",
                newName: "IX_employees_company_id_work_email");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "employees",
                table: "position_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_managerial",
                schema: "employees",
                table: "position_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "manager_id",
                schema: "employees",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "personal_email",
                schema: "employees",
                table: "employees",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "start_date",
                schema: "employees",
                table: "employees",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "employees",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "employees",
                table: "departments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "employees",
                table: "departments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "manager_employee_id",
                schema: "employees",
                table: "departments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_department_id",
                schema: "employees",
                table: "departments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_employees_company_id_status",
                schema: "employees",
                table: "employees",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_employees_manager_id",
                schema: "employees",
                table: "employees",
                column: "manager_id");

            migrationBuilder.CreateIndex(
                name: "IX_departments_parent_department_id",
                schema: "employees",
                table: "departments",
                column: "parent_department_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_employees_company_id_status",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "IX_employees_manager_id",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "IX_departments_parent_department_id",
                schema: "employees",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "employees",
                table: "position_profiles");

            migrationBuilder.DropColumn(
                name: "is_managerial",
                schema: "employees",
                table: "position_profiles");

            migrationBuilder.DropColumn(
                name: "manager_id",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "personal_email",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "start_date",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "employees",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "employees",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "manager_employee_id",
                schema: "employees",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "parent_department_id",
                schema: "employees",
                table: "departments");

            migrationBuilder.RenameColumn(
                name: "work_email",
                schema: "employees",
                table: "employees",
                newName: "email");

            migrationBuilder.RenameIndex(
                name: "IX_employees_company_id_work_email",
                schema: "employees",
                table: "employees",
                newName: "IX_employees_company_id_email");

            migrationBuilder.AddColumn<DateOnly>(
                name: "hired_on",
                schema: "employees",
                table: "employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "employees",
                table: "employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
