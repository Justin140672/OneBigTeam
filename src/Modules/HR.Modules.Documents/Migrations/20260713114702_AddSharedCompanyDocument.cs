using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedCompanyDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shared_company_documents",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    current_file_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    review_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shared_company_documents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shared_company_documents_company_id",
                schema: "documents",
                table: "shared_company_documents",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_shared_company_documents_company_id_category",
                schema: "documents",
                table: "shared_company_documents",
                columns: new[] { "company_id", "category" });

            migrationBuilder.CreateIndex(
                name: "IX_shared_company_documents_company_id_status",
                schema: "documents",
                table: "shared_company_documents",
                columns: new[] { "company_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shared_company_documents",
                schema: "documents");
        }
    }
}
