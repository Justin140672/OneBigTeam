using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddIsConfirmedToSharedCompanyDocumentAcknowledgement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_confirmed",
                schema: "documents",
                table: "shared_company_document_acknowledgements",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_confirmed",
                schema: "documents",
                table: "shared_company_document_acknowledgements");
        }
    }
}
