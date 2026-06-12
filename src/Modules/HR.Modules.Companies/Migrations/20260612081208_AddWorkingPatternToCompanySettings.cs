using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkingPatternToCompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "working_week",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.AddColumn<decimal>(
                name: "hours_per_day",
                schema: "companies",
                table: "company_settings",
                type: "numeric(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "working_days",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hours_per_day",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "working_days",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.AddColumn<string>(
                name: "working_week",
                schema: "companies",
                table: "company_settings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");
        }
    }
}
