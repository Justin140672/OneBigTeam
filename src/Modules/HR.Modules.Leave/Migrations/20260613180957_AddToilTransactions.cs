using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Leave.Migrations
{
    /// <inheritdoc />
    public partial class AddToilTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "toil_transactions",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_balance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    awarded_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    days = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_toil_transactions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_toil_transactions_company_id_employee_id",
                schema: "leave",
                table: "toil_transactions",
                columns: new[] { "company_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "IX_toil_transactions_company_id_employee_id_occurred_on",
                schema: "leave",
                table: "toil_transactions",
                columns: new[] { "company_id", "employee_id", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "IX_toil_transactions_company_id_leave_balance_id",
                schema: "leave",
                table: "toil_transactions",
                columns: new[] { "company_id", "leave_balance_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "toil_transactions",
                schema: "leave");
        }
    }
}
