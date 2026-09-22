using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.DataImport.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrphanedImportFileUploadIntentTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cleared_at",
                schema: "data_import",
                table: "orphaned_import_file_uploads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "confirmed_at",
                schema: "data_import",
                table: "orphaned_import_file_uploads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_orphaned_import_file_uploads_confirmed_at_deleted_at_cleare~",
                schema: "data_import",
                table: "orphaned_import_file_uploads",
                columns: new[] { "confirmed_at", "deleted_at", "cleared_at", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orphaned_import_file_uploads_confirmed_at_deleted_at_cleare~",
                schema: "data_import",
                table: "orphaned_import_file_uploads");

            migrationBuilder.DropColumn(
                name: "cleared_at",
                schema: "data_import",
                table: "orphaned_import_file_uploads");

            migrationBuilder.DropColumn(
                name: "confirmed_at",
                schema: "data_import",
                table: "orphaned_import_file_uploads");
        }
    }
}
