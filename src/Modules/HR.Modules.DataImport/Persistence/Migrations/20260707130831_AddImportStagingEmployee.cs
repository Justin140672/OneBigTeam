using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.DataImport.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportStagingEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_staging_employees",
                schema: "data_import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_number = table.Column<int>(type: "integer", nullable: false),
                    employee_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    work_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    manager_reference = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    raw_data = table.Column<string>(type: "text", nullable: false),
                    is_valid = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_staging_employees", x => x.id);
                    table.ForeignKey(
                        name: "FK_import_staging_employees_import_sessions_import_session_id",
                        column: x => x.import_session_id,
                        principalSchema: "data_import",
                        principalTable: "import_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_import_staging_employees_company_id",
                schema: "data_import",
                table: "import_staging_employees",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_staging_employees_import_session_id",
                schema: "data_import",
                table: "import_staging_employees",
                column: "import_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_staging_employees_import_session_id_employee_number",
                schema: "data_import",
                table: "import_staging_employees",
                columns: new[] { "import_session_id", "employee_number" });

            migrationBuilder.CreateIndex(
                name: "IX_import_staging_employees_import_session_id_work_email",
                schema: "data_import",
                table: "import_staging_employees",
                columns: new[] { "import_session_id", "work_email" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_staging_employees",
                schema: "data_import");
        }
    }
}
