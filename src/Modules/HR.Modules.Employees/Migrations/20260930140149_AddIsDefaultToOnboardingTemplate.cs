using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddIsDefaultToOnboardingTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_onboarding_templates_company_id",
                schema: "employees",
                table: "onboarding_templates");

            migrationBuilder.AddColumn<bool>(
                name: "is_default",
                schema: "employees",
                table: "onboarding_templates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Supabase Data API classification: server-only/direct PostgreSQL. Adds a column and index
            // to the existing employees.onboarding_templates table; no grants are added.
            // Backfill: per company, prefer the active "Standard Onboarding" template, else the oldest
            // active template, else the oldest template overall.
            migrationBuilder.Sql(@"
                WITH ranked_templates AS (
                    SELECT
                        id,
                        ROW_NUMBER() OVER (
                            PARTITION BY company_id
                            ORDER BY
                                (is_active AND name = 'Standard Onboarding') DESC,
                                is_active DESC,
                                created_at ASC,
                                id ASC
                        ) AS rn
                    FROM employees.onboarding_templates
                )
                UPDATE employees.onboarding_templates AS t
                SET is_default = TRUE
                FROM ranked_templates AS r
                WHERE t.id = r.id
                  AND r.rn = 1;
            ");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_templates_company_id_is_default",
                schema: "employees",
                table: "onboarding_templates",
                column: "company_id",
                unique: true,
                filter: "is_default");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_onboarding_templates_company_id_is_default",
                schema: "employees",
                table: "onboarding_templates");

            migrationBuilder.DropColumn(
                name: "is_default",
                schema: "employees",
                table: "onboarding_templates");

            migrationBuilder.CreateIndex(
                name: "IX_onboarding_templates_company_id",
                schema: "employees",
                table: "onboarding_templates",
                column: "company_id");
        }
    }
}
