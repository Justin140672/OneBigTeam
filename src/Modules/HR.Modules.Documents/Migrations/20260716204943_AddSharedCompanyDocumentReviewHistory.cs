using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedCompanyDocumentReviewHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shared_company_document_review_histories",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shared_company_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reviewed_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    previous_review_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shared_company_document_review_histories", x => x.id);
                    table.ForeignKey(
                        name: "FK_shared_company_document_review_histories_shared_company_doc~",
                        column: x => x.shared_company_document_id,
                        principalSchema: "documents",
                        principalTable: "shared_company_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shared_company_document_review_histories_company_id",
                schema: "documents",
                table: "shared_company_document_review_histories",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_shared_company_document_review_histories_shared_company_doc~",
                schema: "documents",
                table: "shared_company_document_review_histories",
                columns: new[] { "shared_company_document_id", "review_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shared_company_document_review_histories",
                schema: "documents");
        }
    }
}
