using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedCompanyDocumentAcknowledgementDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "acknowledgement_statement",
                schema: "documents",
                table: "shared_company_document_acknowledgements",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "task_id",
                schema: "documents",
                table: "shared_company_document_acknowledgements",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "acknowledgement_statement",
                schema: "documents",
                table: "shared_company_document_acknowledgements");

            migrationBuilder.DropColumn(
                name: "task_id",
                schema: "documents",
                table: "shared_company_document_acknowledgements");
        }
    }
}
