using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.DataImport.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrphanedImportFileUploadDeletionEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deletion_eligible_at",
                schema: "data_import",
                table: "orphaned_import_file_uploads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "data_import",
                table: "orphaned_import_file_uploads",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_orphaned_import_file_uploads_deletion_eligible_at_confirmed~",
                schema: "data_import",
                table: "orphaned_import_file_uploads",
                columns: new[] { "deletion_eligible_at", "confirmed_at", "cleared_at", "deleted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orphaned_import_file_uploads_deletion_eligible_at_confirmed~",
                schema: "data_import",
                table: "orphaned_import_file_uploads");

            migrationBuilder.DropColumn(
                name: "deletion_eligible_at",
                schema: "data_import",
                table: "orphaned_import_file_uploads");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "data_import",
                table: "orphaned_import_file_uploads");
        }
    }
}
