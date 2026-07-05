using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddCompensation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "compensations",
                schema: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    salary_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    salary = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    hours_per_week = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    fte = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compensations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_compensations_company_id",
                schema: "employees",
                table: "compensations",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_compensations_company_id_employee_id",
                schema: "employees",
                table: "compensations",
                columns: new[] { "company_id", "employee_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "compensations",
                schema: "employees");
        }
    }
}
