using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedCompanyDocumentVersionNoteAndAcknowledgement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "requires_acknowledgement",
                schema: "documents",
                table: "shared_company_document_versions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "version_note",
                schema: "documents",
                table: "shared_company_document_versions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "requires_acknowledgement",
                schema: "documents",
                table: "shared_company_document_versions");

            migrationBuilder.DropColumn(
                name: "version_note",
                schema: "documents",
                table: "shared_company_document_versions");
        }
    }
}
