using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Offboarding.Migrations
{
    /// <inheritdoc />
    public partial class AddBackdatedDepartureReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "requires_hr_confirmation",
                schema: "offboarding",
                table: "offboarding_tasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_backdated",
                schema: "offboarding",
                table: "offboarding_plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "requires_hr_reconciliation",
                schema: "offboarding",
                table: "offboarding_plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_plans_company_id_requires_hr_reconciliation",
                schema: "offboarding",
                table: "offboarding_plans",
                columns: new[] { "company_id", "requires_hr_reconciliation" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_offboarding_plans_company_id_requires_hr_reconciliation",
                schema: "offboarding",
                table: "offboarding_plans");

            migrationBuilder.DropColumn(
                name: "requires_hr_confirmation",
                schema: "offboarding",
                table: "offboarding_tasks");

            migrationBuilder.DropColumn(
                name: "is_backdated",
                schema: "offboarding",
                table: "offboarding_plans");

            migrationBuilder.DropColumn(
                name: "requires_hr_reconciliation",
                schema: "offboarding",
                table: "offboarding_plans");
        }
    }
}
