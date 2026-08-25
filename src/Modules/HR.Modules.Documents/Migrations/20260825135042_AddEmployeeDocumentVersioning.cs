using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeDocumentVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_latest_version",
                schema: "documents",
                table: "employee_documents",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "previous_version_id",
                schema: "documents",
                table: "employee_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_documents_company_id_employee_id_is_latest_version",
                schema: "documents",
                table: "employee_documents",
                columns: new[] { "company_id", "employee_id", "is_latest_version" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_documents_previous_version_id",
                schema: "documents",
                table: "employee_documents",
                column: "previous_version_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_employee_documents_employee_documents_previous_version_id",
                schema: "documents",
                table: "employee_documents",
                column: "previous_version_id",
                principalSchema: "documents",
                principalTable: "employee_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_employee_documents_employee_documents_previous_version_id",
                schema: "documents",
                table: "employee_documents");

            migrationBuilder.DropIndex(
                name: "IX_employee_documents_company_id_employee_id_is_latest_version",
                schema: "documents",
                table: "employee_documents");

            migrationBuilder.DropIndex(
                name: "IX_employee_documents_previous_version_id",
                schema: "documents",
                table: "employee_documents");

            migrationBuilder.DropColumn(
                name: "is_latest_version",
                schema: "documents",
                table: "employee_documents");

            migrationBuilder.DropColumn(
                name: "previous_version_id",
                schema: "documents",
                table: "employee_documents");
        }
    }
}
