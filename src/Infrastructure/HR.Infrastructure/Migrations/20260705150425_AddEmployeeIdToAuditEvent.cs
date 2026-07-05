using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeIdToAuditEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "employee_id",
                schema: "audit",
                table: "audit_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_company_id_employee_id_occurred_at",
                schema: "audit",
                table: "audit_events",
                columns: new[] { "company_id", "employee_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_events_company_id_employee_id_occurred_at",
                schema: "audit",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "employee_id",
                schema: "audit",
                table: "audit_events");
        }
    }
}
