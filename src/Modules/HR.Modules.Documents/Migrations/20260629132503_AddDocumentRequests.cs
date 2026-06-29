using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_requests",
                schema: "documents",
                columns: table => new
                {
                    id                                    = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id                            = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id                           = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type_id                      = table.Column<Guid>(type: "uuid", nullable: false),
                    position_profile_required_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status                                = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    due_date                              = table.Column<DateOnly>(type: "date", nullable: true),
                    requested_by_employee_id              = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at                            = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at                          = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_by_employee_id              = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_requests_company_id_employee_id",
                schema: "documents",
                table: "document_requests",
                columns: new[] { "company_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "IX_document_requests_employee_id_document_type_id",
                schema: "documents",
                table: "document_requests",
                columns: new[] { "employee_id", "document_type_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_requests",
                schema: "documents");
        }
    }
}
