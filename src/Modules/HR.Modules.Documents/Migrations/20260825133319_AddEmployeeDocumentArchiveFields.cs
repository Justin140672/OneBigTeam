using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeDocumentArchiveFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "archive_reason",
                schema: "documents",
                table: "employee_documents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                schema: "documents",
                table: "employee_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "archived_by_user_id",
                schema: "documents",
                table: "employee_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_archived",
                schema: "documents",
                table: "employee_documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "restored_at",
                schema: "documents",
                table: "employee_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "restored_by_user_id",
                schema: "documents",
                table: "employee_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_documents_company_id_is_archived",
                schema: "documents",
                table: "employee_documents",
                columns: new[] { "company_id", "is_archived" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_employee_documents_company_id_is_archived",
                schema: "documents",
                table: "employee_documents");

            migrationBuilder.DropColumn(
                name: "archive_reason",
                schema: "documents",
                table: "employee_documents");

            migrationBuilder.DropColumn(
                name: "archived_at",
                schema: "documents",
                table: "employee_documents");

            migrationBuilder.DropColumn(
                name: "archived_by_user_id",
                schema: "documents",
                table: "employee_documents");

            migrationBuilder.DropColumn(
                name: "is_archived",
                schema: "documents",
                table: "employee_documents");

            migrationBuilder.DropColumn(
                name: "restored_at",
                schema: "documents",
                table: "employee_documents");

            migrationBuilder.DropColumn(
                name: "restored_by_user_id",
                schema: "documents",
                table: "employee_documents");
        }
    }
}
