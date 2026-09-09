using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Reporting.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganisationDataExportArtefactCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "attempt_files_cleaned_at",
                schema: "reporting",
                table: "organisation_data_exports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_organisation_data_exports_status_attempt_files_cleaned_at",
                schema: "reporting",
                table: "organisation_data_exports",
                columns: new[] { "status", "attempt_files_cleaned_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_organisation_data_exports_status_attempt_files_cleaned_at",
                schema: "reporting",
                table: "organisation_data_exports");

            migrationBuilder.DropColumn(
                name: "attempt_files_cleaned_at",
                schema: "reporting",
                table: "organisation_data_exports");
        }
    }
}
