using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "documents");

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "documents",
                columns: table => new
                {
                    id           = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id   = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id  = table.Column<Guid>(type: "uuid", nullable: true),
                    title        = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description  = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    document_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    file_name    = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_size    = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    storage_key  = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status       = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expiry_date  = table.Column<DateOnly>(type: "date", nullable: true),
                    uploaded_by  = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at   = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at   = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_documents_company_id",
                schema: "documents",
                table: "documents",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_documents_employee_id",
                schema: "documents",
                table: "documents",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "IX_documents_company_id_employee_id_status",
                schema: "documents",
                table: "documents",
                columns: new[] { "company_id", "employee_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documents",
                schema: "documents");
        }
    }
}
