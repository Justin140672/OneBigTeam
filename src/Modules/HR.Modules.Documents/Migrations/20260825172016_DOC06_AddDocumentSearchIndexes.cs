using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class DOC06_AddDocumentSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_employee_documents_company_id_created_at",
                schema: "documents",
                table: "employee_documents",
                columns: new[] { "company_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_documents_company_id_expiry_date",
                schema: "documents",
                table: "employee_documents",
                columns: new[] { "company_id", "expiry_date" });

            migrationBuilder.CreateIndex(
                name: "IX_documents_company_id_document_type_id",
                schema: "documents",
                table: "documents",
                columns: new[] { "company_id", "document_type_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_employee_documents_company_id_created_at",
                schema: "documents",
                table: "employee_documents");

            migrationBuilder.DropIndex(
                name: "IX_employee_documents_company_id_expiry_date",
                schema: "documents",
                table: "employee_documents");

            migrationBuilder.DropIndex(
                name: "IX_documents_company_id_document_type_id",
                schema: "documents",
                table: "documents");
        }
    }
}
