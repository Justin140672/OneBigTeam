using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.DataImport.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrphanedImportFileUpload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orphaned_import_file_uploads",
                schema: "data_import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orphaned_import_file_uploads", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_orphaned_import_file_uploads_company_id",
                schema: "data_import",
                table: "orphaned_import_file_uploads",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_orphaned_import_file_uploads_deleted_at",
                schema: "data_import",
                table: "orphaned_import_file_uploads",
                column: "deleted_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orphaned_import_file_uploads",
                schema: "data_import");
        }
    }
}
