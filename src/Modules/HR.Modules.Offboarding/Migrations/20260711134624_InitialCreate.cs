using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Offboarding.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "offboarding");

            migrationBuilder.CreateTable(
                name: "offboarding_plans",
                schema: "offboarding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_working_day = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offboarding_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "offboarding_tasks",
                schema: "offboarding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offboarding_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    assign_to = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offboarding_tasks", x => x.id);
                    table.ForeignKey(
                        name: "FK_offboarding_tasks_offboarding_plans_offboarding_plan_id",
                        column: x => x.offboarding_plan_id,
                        principalSchema: "offboarding",
                        principalTable: "offboarding_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_offboarding_plans_company_id",
                schema: "offboarding",
                table: "offboarding_plans",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_offboarding_plans_company_id_employee_id",
                schema: "offboarding",
                table: "offboarding_plans",
                columns: new[] { "company_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "IX_offboarding_plans_company_id_status",
                schema: "offboarding",
                table: "offboarding_plans",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_offboarding_tasks_company_id_offboarding_plan_id",
                schema: "offboarding",
                table: "offboarding_tasks",
                columns: new[] { "company_id", "offboarding_plan_id" });

            migrationBuilder.CreateIndex(
                name: "IX_offboarding_tasks_offboarding_plan_id",
                schema: "offboarding",
                table: "offboarding_tasks",
                column: "offboarding_plan_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offboarding_tasks",
                schema: "offboarding");

            migrationBuilder.DropTable(
                name: "offboarding_plans",
                schema: "offboarding");
        }
    }
}
