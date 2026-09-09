using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Reporting.Migrations
{
    /// <inheritdoc />
    public partial class HardenOrganisationDataExportRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                schema: "reporting",
                table: "organisation_data_exports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_attempt_at",
                schema: "reporting",
                table: "organisation_data_exports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "missing_document_count",
                schema: "reporting",
                table: "organisation_data_exports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "reporting",
                table: "organisation_data_exports",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ix_organisation_data_exports_active_per_company",
                schema: "reporting",
                table: "organisation_data_exports",
                column: "company_id",
                unique: true,
                filter: "status IN ('Pending', 'InProgress')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_organisation_data_exports_active_per_company",
                schema: "reporting",
                table: "organisation_data_exports");

            migrationBuilder.DropColumn(
                name: "attempt_count",
                schema: "reporting",
                table: "organisation_data_exports");

            migrationBuilder.DropColumn(
                name: "last_attempt_at",
                schema: "reporting",
                table: "organisation_data_exports");

            migrationBuilder.DropColumn(
                name: "missing_document_count",
                schema: "reporting",
                table: "organisation_data_exports");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "reporting",
                table: "organisation_data_exports");
        }
    }
}
