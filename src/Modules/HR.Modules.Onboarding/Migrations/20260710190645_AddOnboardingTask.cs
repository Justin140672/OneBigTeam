using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Onboarding.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "onboarding_tasks",
                schema: "onboarding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    onboarding_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    assign_to = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_onboarding_tasks", x => x.id);
                    table.ForeignKey(
                        name: "FK_onboarding_tasks_onboarding_plans_onboarding_plan_id",
                        column: x => x.onboarding_plan_id,
                        principalSchema: "onboarding",
                        principalTable: "onboarding_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_onboarding_tasks_company_id_onboarding_plan_id",
                schema: "onboarding",
                table: "onboarding_tasks",
                columns: new[] { "company_id", "onboarding_plan_id" });

            migrationBuilder.CreateIndex(
                name: "IX_onboarding_tasks_onboarding_plan_id",
                schema: "onboarding",
                table: "onboarding_tasks",
                column: "onboarding_plan_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "onboarding_tasks",
                schema: "onboarding");
        }
    }
}
