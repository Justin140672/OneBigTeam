using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Probation.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "probation");

            migrationBuilder.CreateTable(
                name: "probation_records",
                schema: "probation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expected_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    extension_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    decision_date = table.Column<DateOnly>(type: "date", nullable: true),
                    decision_maker_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outcome_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_probation_records", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_probation_records_company_id",
                schema: "probation",
                table: "probation_records",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_probation_records_company_id_employee_id",
                schema: "probation",
                table: "probation_records",
                columns: new[] { "company_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "IX_probation_records_company_id_manager_employee_id",
                schema: "probation",
                table: "probation_records",
                columns: new[] { "company_id", "manager_employee_id" });

            migrationBuilder.CreateIndex(
                name: "IX_probation_records_company_id_status",
                schema: "probation",
                table: "probation_records",
                columns: new[] { "company_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "probation_records",
                schema: "probation");
        }
    }
}
