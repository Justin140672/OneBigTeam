using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class Ticket23_AddCandidateDocumentDeletionOperationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "causation_id",
                schema: "recruitment",
                table: "candidate_document_deletion_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                schema: "recruitment",
                table: "candidate_document_deletion_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "message_id",
                schema: "recruitment",
                table: "candidate_document_deletion_operations",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "causation_id",
                schema: "recruitment",
                table: "candidate_document_deletion_operations");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "recruitment",
                table: "candidate_document_deletion_operations");

            migrationBuilder.DropColumn(
                name: "message_id",
                schema: "recruitment",
                table: "candidate_document_deletion_operations");
        }
    }
}
