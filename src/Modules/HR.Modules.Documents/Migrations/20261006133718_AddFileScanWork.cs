using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddFileScanWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_scan_work",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<int>(type: "integer", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    dispatch_count = table.Column<int>(type: "integer", nullable: false),
                    lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_scan_work", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_file_scan_work_state_lease_expires_at",
                schema: "documents",
                table: "file_scan_work",
                columns: new[] { "state", "lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_file_scan_work_state_next_attempt_at",
                schema: "documents",
                table: "file_scan_work",
                columns: new[] { "state", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ux_file_scan_work_target_entity",
                schema: "documents",
                table: "file_scan_work",
                columns: new[] { "target_type", "entity_id" },
                unique: true);

            foreach (var (table, targetType) in new[]
            {
                ("documents", 0),
                ("employee_profile_photos", 1),
                ("pending_profile_photos", 2),
                ("shared_company_documents", 3),
                ("shared_company_document_versions", 4),
            })
            {
                migrationBuilder.Sql($@"
                    INSERT INTO documents.file_scan_work
                        (id, target_type, entity_id, company_id, state, attempt_count, dispatch_count, next_attempt_at, created_at, updated_at, version)
                    SELECT gen_random_uuid(), {targetType}, id, company_id, 0, 0, 0, now(), now(), now(), 1
                    FROM documents.{table}
                    WHERE scan_status IN ('Pending', 'Scanning')
                    ON CONFLICT DO NOTHING;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_scan_work",
                schema: "documents");
        }
    }
}
