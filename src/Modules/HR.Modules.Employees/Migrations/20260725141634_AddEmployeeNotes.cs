using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_notes",
                schema: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    is_important = table.Column<bool>(type: "boolean", nullable: false),
                    is_superseded = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    superseded_by_note_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_notes", x => x.id);
                    table.ForeignKey(
                        name: "FK_employee_notes_employee_notes_superseded_by_note_id",
                        column: x => x.superseded_by_note_id,
                        principalSchema: "employees",
                        principalTable: "employee_notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_notes_company_id",
                schema: "employees",
                table: "employee_notes",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_employee_notes_company_id_employee_id",
                schema: "employees",
                table: "employee_notes",
                columns: new[] { "company_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_notes_superseded_by_note_id",
                schema: "employees",
                table: "employee_notes",
                column: "superseded_by_note_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_notes",
                schema: "employees");
        }
    }
}
