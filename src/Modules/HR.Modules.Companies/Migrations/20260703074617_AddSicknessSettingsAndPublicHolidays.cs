using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddSicknessSettingsAndPublicHolidays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "exclude_public_holidays_from_sickness",
                schema: "companies",
                table: "company_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "fit_note_required_after_days",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "return_to_work_required_after_days",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "public_holidays",
                schema: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    country_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_holidays", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_public_holidays_company_id_date",
                schema: "companies",
                table: "public_holidays",
                columns: new[] { "company_id", "date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "public_holidays",
                schema: "companies");

            migrationBuilder.DropColumn(
                name: "exclude_public_holidays_from_sickness",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "fit_note_required_after_days",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "return_to_work_required_after_days",
                schema: "companies",
                table: "company_settings");
        }
    }
}
