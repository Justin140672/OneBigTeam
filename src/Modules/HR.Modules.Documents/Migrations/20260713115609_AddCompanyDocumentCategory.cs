using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyDocumentCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_shared_company_documents_company_id_category",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.DropColumn(
                name: "category",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                schema: "documents",
                table: "shared_company_documents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "company_document_categories",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_document_categories", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shared_company_documents_category_id",
                schema: "documents",
                table: "shared_company_documents",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_shared_company_documents_company_id_category_id",
                schema: "documents",
                table: "shared_company_documents",
                columns: new[] { "company_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "IX_company_document_categories_company_id",
                schema: "documents",
                table: "company_document_categories",
                column: "company_id");

            migrationBuilder.AddForeignKey(
                name: "FK_shared_company_documents_company_document_categories_catego~",
                schema: "documents",
                table: "shared_company_documents",
                column: "category_id",
                principalSchema: "documents",
                principalTable: "company_document_categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_shared_company_documents_company_document_categories_catego~",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.DropTable(
                name: "company_document_categories",
                schema: "documents");

            migrationBuilder.DropIndex(
                name: "IX_shared_company_documents_category_id",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.DropIndex(
                name: "IX_shared_company_documents_company_id_category_id",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.DropColumn(
                name: "category_id",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.AddColumn<string>(
                name: "category",
                schema: "documents",
                table: "shared_company_documents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_shared_company_documents_company_id_category",
                schema: "documents",
                table: "shared_company_documents",
                columns: new[] { "company_id", "category" });
        }
    }
}
