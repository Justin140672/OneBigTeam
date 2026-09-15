using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeLeavingProcessFinalisationCompletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "finalisation_completed_at",
                schema: "employees",
                table: "employee_leaving_processes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_leaving_processes_status_finalisation_completed_at",
                schema: "employees",
                table: "employee_leaving_processes",
                columns: new[] { "status", "finalisation_completed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_employee_leaving_processes_status_finalisation_completed_at",
                schema: "employees",
                table: "employee_leaving_processes");

            migrationBuilder.DropColumn(
                name: "finalisation_completed_at",
                schema: "employees",
                table: "employee_leaving_processes");
        }
    }
}
