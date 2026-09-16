using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidatePurgeDurableOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "candidate_document_deletion_operations",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_candidate_document_deletion_operations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "candidate_purge_audit_deliveries",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purged_by = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_ids_json = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_candidate_purge_audit_deliveries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_candidate_document_deletion_operations_candidate_id",
                schema: "recruitment",
                table: "candidate_document_deletion_operations",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "IX_candidate_document_deletion_operations_company_id_status",
                schema: "recruitment",
                table: "candidate_document_deletion_operations",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_candidate_purge_audit_deliveries_company_id_status",
                schema: "recruitment",
                table: "candidate_purge_audit_deliveries",
                columns: new[] { "company_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "candidate_document_deletion_operations",
                schema: "recruitment");

            migrationBuilder.DropTable(
                name: "candidate_purge_audit_deliveries",
                schema: "recruitment");
        }
    }
}
