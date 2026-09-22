using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Support.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportAttachmentPendingDeletions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "support_attachment_pending_deletions",
                schema: "support",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    last_failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_attachment_pending_deletions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_support_attachment_pending_deletions_resolved_at",
                schema: "support",
                table: "support_attachment_pending_deletions",
                column: "resolved_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "support_attachment_pending_deletions",
                schema: "support");
        }
    }
}
