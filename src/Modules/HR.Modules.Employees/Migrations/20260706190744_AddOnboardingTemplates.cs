using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "onboarding_template_id",
                schema: "employees",
                table: "position_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "onboarding_templates",
                schema: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_onboarding_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "onboarding_template_tasks",
                schema: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    onboarding_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    assign_to = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    due_days_after_start = table.Column<int>(type: "integer", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_onboarding_template_tasks", x => x.id);
                    table.ForeignKey(
                        name: "FK_onboarding_template_tasks_onboarding_templates_onboarding_t~",
                        column: x => x.onboarding_template_id,
                        principalSchema: "employees",
                        principalTable: "onboarding_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_onboarding_template_tasks_company_id",
                schema: "employees",
                table: "onboarding_template_tasks",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_onboarding_template_tasks_onboarding_template_id",
                schema: "employees",
                table: "onboarding_template_tasks",
                column: "onboarding_template_id");

            migrationBuilder.CreateIndex(
                name: "IX_onboarding_templates_company_id",
                schema: "employees",
                table: "onboarding_templates",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_onboarding_templates_company_id_name",
                schema: "employees",
                table: "onboarding_templates",
                columns: new[] { "company_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "onboarding_template_tasks",
                schema: "employees");

            migrationBuilder.DropTable(
                name: "onboarding_templates",
                schema: "employees");

            migrationBuilder.DropColumn(
                name: "onboarding_template_id",
                schema: "employees",
                table: "position_profiles");
        }
    }
}
