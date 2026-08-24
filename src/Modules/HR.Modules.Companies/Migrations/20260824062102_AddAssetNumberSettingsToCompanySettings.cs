using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetNumberSettingsToCompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "asset_number_minimum_length",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "asset_number_mode",
                schema: "companies",
                table: "company_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<string>(
                name: "asset_number_prefix",
                schema: "companies",
                table: "company_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "next_asset_number",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_company_settings_asset_number_minimum_length",
                schema: "companies",
                table: "company_settings",
                sql: "asset_number_minimum_length BETWEEN 1 AND 10");

            migrationBuilder.AddCheckConstraint(
                name: "CK_company_settings_next_asset_number",
                schema: "companies",
                table: "company_settings",
                sql: "next_asset_number > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_company_settings_asset_number_minimum_length",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_company_settings_next_asset_number",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "asset_number_minimum_length",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "asset_number_mode",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "asset_number_prefix",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "next_asset_number",
                schema: "companies",
                table: "company_settings");
        }
    }
}
