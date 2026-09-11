using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferDetailsToApplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "offer_date",
                schema: "recruitment",
                table: "applications",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "offer_made_at",
                schema: "recruitment",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "offer_notes",
                schema: "recruitment",
                table: "applications",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "offer_responded_at",
                schema: "recruitment",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "offer_response_status",
                schema: "recruitment",
                table: "applications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "offered_salary",
                schema: "recruitment",
                table: "applications",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "offered_salary_frequency",
                schema: "recruitment",
                table: "applications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "offered_start_date",
                schema: "recruitment",
                table: "applications",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "offer_date",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_made_at",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_notes",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_responded_at",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_response_status",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offered_salary",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offered_salary_frequency",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offered_start_date",
                schema: "recruitment",
                table: "applications");
        }
    }
}
