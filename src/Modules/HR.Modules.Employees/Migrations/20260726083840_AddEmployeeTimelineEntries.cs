using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeTimelineEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_timeline_entries",
                schema: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_date = table.Column<DateOnly>(type: "date", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    performed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_module = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<int>(type: "integer", nullable: false),
                    created_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_timeline_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_employee_timeline_entries_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "employees",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_timeline_entries_company_id",
                schema: "employees",
                table: "employee_timeline_entries",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_employee_timeline_entries_company_id_employee_id",
                schema: "employees",
                table: "employee_timeline_entries",
                columns: new[] { "company_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_timeline_entries_company_id_employee_id_event_type~",
                schema: "employees",
                table: "employee_timeline_entries",
                columns: new[] { "company_id", "employee_id", "event_type", "event_date" },
                unique: true,
                filter: "source_record_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_employee_timeline_entries_company_id_source_module_event_ty~",
                schema: "employees",
                table: "employee_timeline_entries",
                columns: new[] { "company_id", "source_module", "event_type", "source_record_id" },
                unique: true,
                filter: "source_record_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_employee_timeline_entries_employee_id",
                schema: "employees",
                table: "employee_timeline_entries",
                column: "employee_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_timeline_entries",
                schema: "employees");
        }
    }
}
