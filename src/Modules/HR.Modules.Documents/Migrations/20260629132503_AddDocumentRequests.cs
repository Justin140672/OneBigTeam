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
            migrationBuilder.DropColumn(
                name: "due_days_after_start",
                schema: "documents",
                table: "document_requests");

            migrationBuilder.DropColumn(
                name: "is_mandatory",
                schema: "documents",
                table: "document_requests");

            migrationBuilder.DropColumn(
                name: "requires_expiry_date",
                schema: "documents",
                table: "document_requests");

            migrationBuilder.RenameColumn(
                name: "fulfilled_at",
                schema: "documents",
                table: "document_requests",
                newName: "completed_at");

            migrationBuilder.AddColumn<Guid>(
                name: "completed_by_employee_id",
                schema: "documents",
                table: "document_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                schema: "documents",
                table: "document_requests",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "position_profile_required_document_id",
                schema: "documents",
                table: "document_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "requested_by_employee_id",
                schema: "documents",
                table: "document_requests",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "completed_by_employee_id",
                schema: "documents",
                table: "document_requests");

            migrationBuilder.DropColumn(
                name: "due_date",
                schema: "documents",
                table: "document_requests");

            migrationBuilder.DropColumn(
                name: "position_profile_required_document_id",
                schema: "documents",
                table: "document_requests");

            migrationBuilder.DropColumn(
                name: "requested_by_employee_id",
                schema: "documents",
                table: "document_requests");

            migrationBuilder.RenameColumn(
                name: "completed_at",
                schema: "documents",
                table: "document_requests",
                newName: "fulfilled_at");

            migrationBuilder.AddColumn<int>(
                name: "due_days_after_start",
                schema: "documents",
                table: "document_requests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_mandatory",
                schema: "documents",
                table: "document_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "requires_expiry_date",
                schema: "documents",
                table: "document_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
