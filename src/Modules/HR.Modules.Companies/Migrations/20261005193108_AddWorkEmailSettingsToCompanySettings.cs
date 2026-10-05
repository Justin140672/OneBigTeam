using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkEmailSettingsToCompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "work_email_naming_convention",
                schema: "companies",
                table: "company_settings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "FirstNameDotLastName");

            migrationBuilder.AddColumn<string>(
                name: "work_email_primary_domain",
                schema: "companies",
                table: "company_settings",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "work_email_suggestions_enabled",
                schema: "companies",
                table: "company_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "work_email_naming_convention",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "work_email_primary_domain",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "work_email_suggestions_enabled",
                schema: "companies",
                table: "company_settings");
        }
    }
}
