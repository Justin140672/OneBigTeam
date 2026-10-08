using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddLeavingProcessPropagations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leaving_process_propagations",
                schema: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leaving_process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    leaving_date = table.Column<DateOnly>(type: "date", nullable: true),
                    last_working_day = table.Column<DateOnly>(type: "date", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    causation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leaving_process_propagations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_leaving_process_propagations_company_id_employee_id_created~",
                schema: "employees",
                table: "leaving_process_propagations",
                columns: new[] { "company_id", "employee_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_leaving_process_propagations_leaving_process_id",
                schema: "employees",
                table: "leaving_process_propagations",
                column: "leaving_process_id");

            migrationBuilder.CreateIndex(
                name: "IX_leaving_process_propagations_status_next_attempt_at",
                schema: "employees",
                table: "leaving_process_propagations",
                columns: new[] { "status", "next_attempt_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leaving_process_propagations",
                schema: "employees");
        }
    }
}
