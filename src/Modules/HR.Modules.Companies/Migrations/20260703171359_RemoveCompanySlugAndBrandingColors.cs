using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCompanySlugAndBrandingColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_company_branding_accent_color",
                schema: "companies",
                table: "company_branding");

            migrationBuilder.DropCheckConstraint(
                name: "CK_company_branding_primary_color",
                schema: "companies",
                table: "company_branding");

            migrationBuilder.DropCheckConstraint(
                name: "CK_company_branding_secondary_color",
                schema: "companies",
                table: "company_branding");

            migrationBuilder.DropIndex(
                name: "IX_companies_slug",
                schema: "companies",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "accent_color",
                schema: "companies",
                table: "company_branding");

            migrationBuilder.DropColumn(
                name: "primary_color",
                schema: "companies",
                table: "company_branding");

            migrationBuilder.DropColumn(
                name: "secondary_color",
                schema: "companies",
                table: "company_branding");

            migrationBuilder.DropColumn(
                name: "slug",
                schema: "companies",
                table: "companies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "accent_color",
                schema: "companies",
                table: "company_branding",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "primary_color",
                schema: "companies",
                table: "company_branding",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "secondary_color",
                schema: "companies",
                table: "company_branding",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "slug",
                schema: "companies",
                table: "companies",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddCheckConstraint(
                name: "CK_company_branding_accent_color",
                schema: "companies",
                table: "company_branding",
                sql: "accent_color ~ '^#[0-9A-Fa-f]{6}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_company_branding_primary_color",
                schema: "companies",
                table: "company_branding",
                sql: "primary_color ~ '^#[0-9A-Fa-f]{6}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_company_branding_secondary_color",
                schema: "companies",
                table: "company_branding",
                sql: "secondary_color ~ '^#[0-9A-Fa-f]{6}$'");

            migrationBuilder.CreateIndex(
                name: "IX_companies_slug",
                schema: "companies",
                table: "companies",
                column: "slug",
                unique: true);
        }
    }
}
