using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Reporting.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganisationDataExportCleanupCursors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "artefact_cleanup_attempt_count",
                schema: "reporting",
                table: "organisation_data_exports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "artefact_cleanup_next_attempt_at",
                schema: "reporting",
                table: "organisation_data_exports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "late_upload_recheck_attempt_count",
                schema: "reporting",
                table: "organisation_data_exports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "late_upload_recheck_next_at",
                schema: "reporting",
                table: "organisation_data_exports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_organisation_data_exports_attempt_files_cleaned_at_artefact~",
                schema: "reporting",
                table: "organisation_data_exports",
                columns: new[] { "attempt_files_cleaned_at", "artefact_cleanup_next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_organisation_data_exports_attempt_files_cleaned_at_late_upl~",
                schema: "reporting",
                table: "organisation_data_exports",
                columns: new[] { "attempt_files_cleaned_at", "late_upload_recheck_next_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_organisation_data_exports_attempt_files_cleaned_at_artefact~",
                schema: "reporting",
                table: "organisation_data_exports");

            migrationBuilder.DropIndex(
                name: "IX_organisation_data_exports_attempt_files_cleaned_at_late_upl~",
                schema: "reporting",
                table: "organisation_data_exports");

            migrationBuilder.DropColumn(
                name: "artefact_cleanup_attempt_count",
                schema: "reporting",
                table: "organisation_data_exports");

            migrationBuilder.DropColumn(
                name: "artefact_cleanup_next_attempt_at",
                schema: "reporting",
                table: "organisation_data_exports");

            migrationBuilder.DropColumn(
                name: "late_upload_recheck_attempt_count",
                schema: "reporting",
                table: "organisation_data_exports");

            migrationBuilder.DropColumn(
                name: "late_upload_recheck_next_at",
                schema: "reporting",
                table: "organisation_data_exports");
        }
    }
}
