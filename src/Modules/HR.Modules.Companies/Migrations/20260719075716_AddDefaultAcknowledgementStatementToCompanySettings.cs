using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultAcknowledgementStatementToCompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "default_acknowledgement_statement",
                schema: "companies",
                table: "company_settings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "I confirm that I have read and understood this document.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_acknowledgement_statement",
                schema: "companies",
                table: "company_settings");
        }
    }
}
