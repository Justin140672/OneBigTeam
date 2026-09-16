using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateDocumentDeletionOperationClaimLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "claimed_by",
                schema: "recruitment",
                table: "candidate_document_deletion_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_terminally_failed",
                schema: "recruitment",
                table: "candidate_document_deletion_operations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_retried_at",
                schema: "recruitment",
                table: "candidate_document_deletion_operations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "last_retried_by_actor_id",
                schema: "recruitment",
                table: "candidate_document_deletion_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_retry_reason",
                schema: "recruitment",
                table: "candidate_document_deletion_operations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_expires_at",
                schema: "recruitment",
                table: "candidate_document_deletion_operations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "recruitment",
                table: "candidate_document_deletion_operations",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "claimed_by",
                schema: "recruitment",
                table: "candidate_document_deletion_operations");

            migrationBuilder.DropColumn(
                name: "is_terminally_failed",
                schema: "recruitment",
                table: "candidate_document_deletion_operations");

            migrationBuilder.DropColumn(
                name: "last_retried_at",
                schema: "recruitment",
                table: "candidate_document_deletion_operations");

            migrationBuilder.DropColumn(
                name: "last_retried_by_actor_id",
                schema: "recruitment",
                table: "candidate_document_deletion_operations");

            migrationBuilder.DropColumn(
                name: "last_retry_reason",
                schema: "recruitment",
                table: "candidate_document_deletion_operations");

            migrationBuilder.DropColumn(
                name: "lease_expires_at",
                schema: "recruitment",
                table: "candidate_document_deletion_operations");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "recruitment",
                table: "candidate_document_deletion_operations");
        }
    }
}
