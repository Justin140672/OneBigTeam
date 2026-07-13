using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedCompanyDocumentAcknowledgementSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "acknowledgement_due_date",
                schema: "documents",
                table: "shared_company_documents",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "acknowledgement_statement",
                schema: "documents",
                table: "shared_company_documents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "acknowledgement_due_date",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.DropColumn(
                name: "acknowledgement_statement",
                schema: "documents",
                table: "shared_company_documents");
        }
    }
}
