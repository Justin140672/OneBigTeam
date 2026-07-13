using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedCompanyDocumentAudienceAndAcknowledgement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.AddColumn<bool>(
                name: "requires_acknowledgement",
                schema: "documents",
                table: "shared_company_documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "shared_company_document_acknowledgements",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shared_company_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shared_company_document_acknowledgements", x => x.id);
                    table.ForeignKey(
                        name: "FK_shared_company_document_acknowledgements_shared_company_doc~",
                        column: x => x.shared_company_document_id,
                        principalSchema: "documents",
                        principalTable: "shared_company_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shared_company_document_versions",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shared_company_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    file_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shared_company_document_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_shared_company_document_versions_shared_company_documents_s~",
                        column: x => x.shared_company_document_id,
                        principalSchema: "documents",
                        principalTable: "shared_company_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shared_company_document_acknowledgements_company_id",
                schema: "documents",
                table: "shared_company_document_acknowledgements",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_shared_company_document_acknowledgements_shared_company_doc~",
                schema: "documents",
                table: "shared_company_document_acknowledgements",
                columns: new[] { "shared_company_document_id", "employee_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shared_company_document_versions_company_id",
                schema: "documents",
                table: "shared_company_document_versions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_shared_company_document_versions_shared_company_document_id~",
                schema: "documents",
                table: "shared_company_document_versions",
                columns: new[] { "shared_company_document_id", "version_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shared_company_document_acknowledgements",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "shared_company_document_versions",
                schema: "documents");

            migrationBuilder.DropColumn(
                name: "audience_department_id",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.DropColumn(
                name: "audience_location_id",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.DropColumn(
                name: "requires_acknowledgement",
                schema: "documents",
                table: "shared_company_documents");
        }
    }
}
