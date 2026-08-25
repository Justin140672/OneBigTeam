using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Offboarding.Migrations
{
    /// <inheritdoc />
    public partial class AddOffboardingReliability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_offboarding_plans_company_id_employee_id",
                schema: "offboarding",
                table: "offboarding_plans");

            migrationBuilder.AddColumn<Guid>(
                name: "assigned_employee_id",
                schema: "offboarding",
                table: "offboarding_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "task_item_created_at",
                schema: "offboarding",
                table: "offboarding_tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_tasks_task_item_created_at",
                schema: "offboarding",
                table: "offboarding_tasks",
                column: "task_item_created_at");

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_plans_company_id_employee_id_active",
                schema: "offboarding",
                table: "offboarding_plans",
                columns: new[] { "company_id", "employee_id" },
                unique: true,
                filter: "status NOT IN ('Completed', 'Cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_offboarding_tasks_task_item_created_at",
                schema: "offboarding",
                table: "offboarding_tasks");

            migrationBuilder.DropIndex(
                name: "ix_offboarding_plans_company_id_employee_id_active",
                schema: "offboarding",
                table: "offboarding_plans");

            migrationBuilder.DropColumn(
                name: "assigned_employee_id",
                schema: "offboarding",
                table: "offboarding_tasks");

            migrationBuilder.DropColumn(
                name: "task_item_created_at",
                schema: "offboarding",
                table: "offboarding_tasks");

            migrationBuilder.CreateIndex(
                name: "IX_offboarding_plans_company_id_employee_id",
                schema: "offboarding",
                table: "offboarding_plans",
                columns: new[] { "company_id", "employee_id" });
        }
    }
}
