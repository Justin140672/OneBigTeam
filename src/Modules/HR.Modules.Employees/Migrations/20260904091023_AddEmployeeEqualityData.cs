using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeEqualityData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_equality_data",
                schema: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gender_identity = table.Column<string>(type: "text", nullable: true),
                    gender_identity_self_described = table.Column<string>(type: "text", nullable: true),
                    married_or_civil_partnership_status = table.Column<string>(type: "text", nullable: true),
                    ethnic_group = table.Column<string>(type: "text", nullable: true),
                    ethnic_group_self_described = table.Column<string>(type: "text", nullable: true),
                    disability_status = table.Column<string>(type: "text", nullable: true),
                    disability_impact = table.Column<string>(type: "text", nullable: true),
                    sexual_orientation = table.Column<string>(type: "text", nullable: true),
                    sexual_orientation_self_described = table.Column<string>(type: "text", nullable: true),
                    religion_or_belief = table.Column<string>(type: "text", nullable: true),
                    religion_or_belief_self_described = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_equality_data", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_equality_data_company_id",
                schema: "employees",
                table: "employee_equality_data",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_employee_equality_data_company_id_employee_id",
                schema: "employees",
                table: "employee_equality_data",
                columns: new[] { "company_id", "employee_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_equality_data",
                schema: "employees");
        }
    }
}
