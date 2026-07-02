using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Sickness.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sickness");

            migrationBuilder.CreateTable(
                name: "sickness_categories",
                schema: "sickness",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sickness_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sickness_records",
                schema: "sickness",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_day_part = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_day_part = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    return_to_work_date = table.Column<DateOnly>(type: "date", nullable: true),
                    evidence_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    evidence_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    total_days = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sickness_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_sickness_records_sickness_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "sickness",
                        principalTable: "sickness_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sickness_categories_company_id",
                schema: "sickness",
                table: "sickness_categories",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_sickness_categories_company_id_display_order",
                schema: "sickness",
                table: "sickness_categories",
                columns: new[] { "company_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_sickness_records_company_id",
                schema: "sickness",
                table: "sickness_records",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_sickness_records_employee_id",
                schema: "sickness",
                table: "sickness_records",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "IX_sickness_records_company_id_status",
                schema: "sickness",
                table: "sickness_records",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_sickness_records_employee_id_start_date",
                schema: "sickness",
                table: "sickness_records",
                columns: new[] { "employee_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "IX_sickness_records_category_id",
                schema: "sickness",
                table: "sickness_records",
                column: "category_id");

            // Seed default sickness categories for Acme (company 00000000-0000-0000-0000-000000000001)
            var now = new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero).ToString("O");
            var companyId = "00000000-0000-0000-0000-000000000001";

            migrationBuilder.InsertData(
                schema: "sickness",
                table: "sickness_categories",
                columns: new[] { "id", "company_id", "name", "is_active", "display_order", "created_at", "updated_at" },
                values: new object[,]
                {
                    { "d0000000-0000-0000-0000-000000000001", companyId, "Illness",              true, 1, now, now },
                    { "d0000000-0000-0000-0000-000000000002", companyId, "Injury",               true, 2, now, now },
                    { "d0000000-0000-0000-0000-000000000003", companyId, "Medical Appointment",  true, 3, now, now },
                    { "d0000000-0000-0000-0000-000000000004", companyId, "Dependent Care",       true, 4, now, now },
                    { "d0000000-0000-0000-0000-000000000005", companyId, "Other",                true, 5, now, now }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sickness_records",
                schema: "sickness");

            migrationBuilder.DropTable(
                name: "sickness_categories",
                schema: "sickness");
        }
    }
}
