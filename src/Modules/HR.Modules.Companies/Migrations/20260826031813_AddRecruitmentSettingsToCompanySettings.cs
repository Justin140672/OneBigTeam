using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddRecruitmentSettingsToCompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "candidate_retention_days",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: false,
                defaultValue: 730);

            migrationBuilder.AddColumn<bool>(
                name: "offer_approval_required",
                schema: "companies",
                table: "company_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "vacancy_approval_required",
                schema: "companies",
                table: "company_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_company_settings_candidate_retention_days",
                schema: "companies",
                table: "company_settings",
                sql: "candidate_retention_days BETWEEN 90 AND 3650");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_company_settings_candidate_retention_days",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "candidate_retention_days",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "offer_approval_required",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "vacancy_approval_required",
                schema: "companies",
                table: "company_settings");
        }
    }
}
