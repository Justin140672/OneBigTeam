using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Support.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportAttachmentPendingDeletionsUniqueStorageKeyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_support_attachment_pending_deletions_storage_key_unresolved",
                schema: "support",
                table: "support_attachment_pending_deletions",
                column: "storage_key",
                unique: true,
                filter: "resolved_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_support_attachment_pending_deletions_storage_key_unresolved",
                schema: "support",
                table: "support_attachment_pending_deletions");
        }
    }
}
