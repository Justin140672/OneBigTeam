using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeLeavingProcess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_leaving_processes",
                schema: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resignation_received_date = table.Column<DateOnly>(type: "date", nullable: false),
                    leaving_date = table.Column<DateOnly>(type: "date", nullable: false),
                    last_working_day = table.Column<DateOnly>(type: "date", nullable: false),
                    notice_period_unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notice_period_length = table.Column<int>(type: "integer", nullable: false),
                    notice_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    leaving_reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_leaving_processes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_leaving_processes_company_id",
                schema: "employees",
                table: "employee_leaving_processes",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_employee_leaving_processes_company_id_employee_id",
                schema: "employees",
                table: "employee_leaving_processes",
                columns: new[] { "company_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_leaving_processes_company_id_employee_id_status",
                schema: "employees",
                table: "employee_leaving_processes",
                columns: new[] { "company_id", "employee_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_leaving_processes",
                schema: "employees");
        }
    }
}
