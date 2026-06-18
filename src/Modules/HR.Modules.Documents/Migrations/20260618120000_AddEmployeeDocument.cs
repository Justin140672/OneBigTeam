using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_documents",
                schema: "documents",
                columns: table => new
                {
                    id              = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id      = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id     = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id     = table.Column<Guid>(type: "uuid", nullable: false),
                    added_by        = table.Column<Guid>(type: "uuid", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at      = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at      = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_documents", x => x.id);
                    table.ForeignKey(
                        name: "FK_employee_documents_documents_document_id",
                        column: x => x.document_id,
                        principalSchema: "documents",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_documents_company_id_employee_id",
                schema: "documents",
                table: "employee_documents",
                columns: new[] { "company_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_documents_employee_id_document_id",
                schema: "documents",
                table: "employee_documents",
                columns: new[] { "employee_id", "document_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_documents",
                schema: "documents");
        }
    }
}
