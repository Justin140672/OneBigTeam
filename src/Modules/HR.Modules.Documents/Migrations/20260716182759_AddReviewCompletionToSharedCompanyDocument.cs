using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewCompletionToSharedCompanyDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "last_review_notes",
                schema: "documents",
                table: "shared_company_documents",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "last_reviewed_at",
                schema: "documents",
                table: "shared_company_documents",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "last_reviewed_by_employee_id",
                schema: "documents",
                table: "shared_company_documents",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_review_notes",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.DropColumn(
                name: "last_reviewed_at",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.DropColumn(
                name: "last_reviewed_by_employee_id",
                schema: "documents",
                table: "shared_company_documents");
        }
    }
}
