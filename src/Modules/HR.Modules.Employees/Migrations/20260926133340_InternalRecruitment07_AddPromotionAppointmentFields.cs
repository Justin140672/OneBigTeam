using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class InternalRecruitment07_AddPromotionAppointmentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Internal recruitment Ticket 7. Supabase Data API classification: server-only/direct
            // PostgreSQL. Adds columns and indexes to the existing employees.employee_promotions
            // table only; no grants to anon/authenticated/service_role are added.
            migrationBuilder.AddColumn<bool>(
                name: "clears_manager",
                schema: "employees",
                table: "employee_promotions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "new_department_id",
                schema: "employees",
                table: "employee_promotions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_reference",
                schema: "employees",
                table: "employee_promotions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_promotions_company_id_source_reference",
                schema: "employees",
                table: "employee_promotions",
                columns: new[] { "company_id", "source_reference" },
                unique: true,
                filter: "source_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_employee_promotions_new_department_id",
                schema: "employees",
                table: "employee_promotions",
                column: "new_department_id");

            migrationBuilder.AddForeignKey(
                name: "FK_employee_promotions_departments_new_department_id",
                schema: "employees",
                table: "employee_promotions",
                column: "new_department_id",
                principalSchema: "employees",
                principalTable: "departments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_employee_promotions_departments_new_department_id",
                schema: "employees",
                table: "employee_promotions");

            migrationBuilder.DropIndex(
                name: "ix_employee_promotions_company_id_source_reference",
                schema: "employees",
                table: "employee_promotions");

            migrationBuilder.DropIndex(
                name: "IX_employee_promotions_new_department_id",
                schema: "employees",
                table: "employee_promotions");

            migrationBuilder.DropColumn(
                name: "clears_manager",
                schema: "employees",
                table: "employee_promotions");

            migrationBuilder.DropColumn(
                name: "new_department_id",
                schema: "employees",
                table: "employee_promotions");

            migrationBuilder.DropColumn(
                name: "source_reference",
                schema: "employees",
                table: "employee_promotions");
        }
    }
}
