using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedCompanyDocumentAudienceRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "audience_department_id",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.DropColumn(
                name: "audience_location_id",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.CreateTable(
                name: "shared_company_document_audience_rules",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shared_company_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shared_company_document_audience_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_shared_company_document_audience_rules_shared_company_docum~",
                        column: x => x.shared_company_document_id,
                        principalSchema: "documents",
                        principalTable: "shared_company_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shared_company_document_audience_rules_company_id",
                schema: "documents",
                table: "shared_company_document_audience_rules",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_shared_company_document_audience_rules_shared_company_docum~",
                schema: "documents",
                table: "shared_company_document_audience_rules",
                columns: new[] { "shared_company_document_id", "rule_type", "target_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shared_company_document_audience_rules",
                schema: "documents");

            migrationBuilder.AddColumn<Guid>(
                name: "audience_department_id",
                schema: "documents",
                table: "shared_company_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "audience_location_id",
                schema: "documents",
                table: "shared_company_documents",
                type: "uuid",
                nullable: true);
        }
    }
}
