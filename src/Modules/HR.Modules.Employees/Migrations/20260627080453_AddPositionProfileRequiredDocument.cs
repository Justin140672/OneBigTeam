using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionProfileRequiredDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "position_profile_required_documents",
                schema: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    due_days_after_start = table.Column<int>(type: "integer", nullable: true),
                    requires_expiry_date = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_position_profile_required_documents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_position_profile_required_documents_company_id",
                schema: "employees",
                table: "position_profile_required_documents",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_position_profile_required_documents_document_type_id",
                schema: "employees",
                table: "position_profile_required_documents",
                column: "document_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_position_profile_required_documents_position_profile_id",
                schema: "employees",
                table: "position_profile_required_documents",
                column: "position_profile_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "position_profile_required_documents",
                schema: "employees");
        }
    }
}
