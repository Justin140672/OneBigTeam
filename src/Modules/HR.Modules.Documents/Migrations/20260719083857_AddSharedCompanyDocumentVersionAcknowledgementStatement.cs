using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedCompanyDocumentVersionAcknowledgementStatement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "acknowledgement_statement",
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
                name: "acknowledgement_statement",
                schema: "documents",
                table: "shared_company_document_versions");
        }
    }
}
