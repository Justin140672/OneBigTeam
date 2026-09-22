using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.DataImport.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportSessionFileDeletionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "file_deleted_at",
                schema: "data_import",
                table: "import_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "file_deletion_attempt_count",
                schema: "data_import",
                table: "import_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "file_deletion_last_attempted_at",
                schema: "data_import",
                table: "import_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_import_sessions_file_deleted_at",
                schema: "data_import",
                table: "import_sessions",
                column: "file_deleted_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_import_sessions_file_deleted_at",
                schema: "data_import",
                table: "import_sessions");

            migrationBuilder.DropColumn(
                name: "file_deleted_at",
                schema: "data_import",
                table: "import_sessions");

            migrationBuilder.DropColumn(
                name: "file_deletion_attempt_count",
                schema: "data_import",
                table: "import_sessions");

            migrationBuilder.DropColumn(
                name: "file_deletion_last_attempted_at",
                schema: "data_import",
                table: "import_sessions");
        }
    }
}
