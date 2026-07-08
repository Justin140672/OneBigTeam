using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Leave.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveBalanceAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leave_balance_adjustments",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adjustment_hours = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    comments = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    adjusted_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adjusted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leave_balance_adjustments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_leave_balance_adjustments_company_id_employee_id",
                schema: "leave",
                table: "leave_balance_adjustments",
                columns: new[] { "company_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "IX_leave_balance_adjustments_company_id_employee_id_leave_type~",
                schema: "leave",
                table: "leave_balance_adjustments",
                columns: new[] { "company_id", "employee_id", "leave_type_id", "adjusted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leave_balance_adjustments",
                schema: "leave");
        }
    }
}
