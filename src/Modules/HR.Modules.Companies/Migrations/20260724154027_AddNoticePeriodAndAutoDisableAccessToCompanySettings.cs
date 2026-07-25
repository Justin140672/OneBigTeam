using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddNoticePeriodAndAutoDisableAccessToCompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_disable_access_on_leaving_date",
                schema: "companies",
                table: "company_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "notice_period_length",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "notice_period_unit",
                schema: "companies",
                table: "company_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Months");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_disable_access_on_leaving_date",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "notice_period_length",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "notice_period_unit",
                schema: "companies",
                table: "company_settings");
        }
    }
}
