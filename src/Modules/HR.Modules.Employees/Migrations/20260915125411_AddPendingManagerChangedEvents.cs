using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingManagerChangedEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pending_manager_changed_events",
                schema: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_manager_id = table.Column<Guid>(type: "uuid", nullable: true),
                    new_manager_id = table.Column<Guid>(type: "uuid", nullable: true),
                    leaving_process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_manager_changed_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pending_manager_changed_events_company_id",
                schema: "employees",
                table: "pending_manager_changed_events",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_pending_manager_changed_events_published_at",
                schema: "employees",
                table: "pending_manager_changed_events",
                column: "published_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_manager_changed_events",
                schema: "employees");
        }
    }
}
