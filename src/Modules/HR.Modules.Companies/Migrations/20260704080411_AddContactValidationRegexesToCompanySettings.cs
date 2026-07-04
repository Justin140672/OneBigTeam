using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddContactValidationRegexesToCompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "mobile_regex",
                schema: "companies",
                table: "company_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "^(?:\\+44\\s?|0)7\\d{3}(?:\\s?\\d{3}){2}$");

            migrationBuilder.AddColumn<string>(
                name: "postcode_regex",
                schema: "companies",
                table: "company_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "^[A-Za-z]{1,2}\\d[A-Za-z\\d]?\\s?\\d[A-Za-z]{2}$");

            migrationBuilder.AddColumn<string>(
                name: "telephone_regex",
                schema: "companies",
                table: "company_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "^(?:\\+44\\s?|0)(?:\\d\\s?){9,10}$");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "mobile_regex",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "postcode_regex",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "telephone_regex",
                schema: "companies",
                table: "company_settings");
        }
    }
}
