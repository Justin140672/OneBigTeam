using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddFileScanStatusToDocumentsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "scan_attempt_count",
                schema: "documents",
                table: "shared_company_documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "scan_completed_at",
                schema: "documents",
                table: "shared_company_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scan_failure_reason",
                schema: "documents",
                table: "shared_company_documents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scan_status",
                schema: "documents",
                table: "shared_company_documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Clean");

            migrationBuilder.AddColumn<int>(
                name: "scan_attempt_count",
                schema: "documents",
                table: "shared_company_document_versions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "scan_completed_at",
                schema: "documents",
                table: "shared_company_document_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scan_failure_reason",
                schema: "documents",
                table: "shared_company_document_versions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scan_status",
                schema: "documents",
                table: "shared_company_document_versions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Clean");

            migrationBuilder.AddColumn<int>(
                name: "scan_attempt_count",
                schema: "documents",
                table: "pending_profile_photos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "scan_completed_at",
                schema: "documents",
                table: "pending_profile_photos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scan_failure_reason",
                schema: "documents",
                table: "pending_profile_photos",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scan_status",
                schema: "documents",
                table: "pending_profile_photos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Clean");

            migrationBuilder.AddColumn<int>(
                name: "scan_attempt_count",
                schema: "documents",
                table: "employee_profile_photos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "scan_completed_at",
                schema: "documents",
                table: "employee_profile_photos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scan_failure_reason",
                schema: "documents",
                table: "employee_profile_photos",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scan_status",
                schema: "documents",
                table: "employee_profile_photos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Clean");

            migrationBuilder.AddColumn<int>(
                name: "scan_attempt_count",
                schema: "documents",
                table: "documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "scan_completed_at",
                schema: "documents",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scan_failure_reason",
                schema: "documents",
                table: "documents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scan_status",
                schema: "documents",
                table: "documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Clean");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "scan_attempt_count",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.DropColumn(
                name: "scan_completed_at",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.DropColumn(
                name: "scan_failure_reason",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.DropColumn(
                name: "scan_status",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.DropColumn(
                name: "scan_attempt_count",
                schema: "documents",
                table: "shared_company_document_versions");

            migrationBuilder.DropColumn(
                name: "scan_completed_at",
                schema: "documents",
                table: "shared_company_document_versions");

            migrationBuilder.DropColumn(
                name: "scan_failure_reason",
                schema: "documents",
                table: "shared_company_document_versions");

            migrationBuilder.DropColumn(
                name: "scan_status",
                schema: "documents",
                table: "shared_company_document_versions");

            migrationBuilder.DropColumn(
                name: "scan_attempt_count",
                schema: "documents",
                table: "pending_profile_photos");

            migrationBuilder.DropColumn(
                name: "scan_completed_at",
                schema: "documents",
                table: "pending_profile_photos");

            migrationBuilder.DropColumn(
                name: "scan_failure_reason",
                schema: "documents",
                table: "pending_profile_photos");

            migrationBuilder.DropColumn(
                name: "scan_status",
                schema: "documents",
                table: "pending_profile_photos");

            migrationBuilder.DropColumn(
                name: "scan_attempt_count",
                schema: "documents",
                table: "employee_profile_photos");

            migrationBuilder.DropColumn(
                name: "scan_completed_at",
                schema: "documents",
                table: "employee_profile_photos");

            migrationBuilder.DropColumn(
                name: "scan_failure_reason",
                schema: "documents",
                table: "employee_profile_photos");

            migrationBuilder.DropColumn(
                name: "scan_status",
                schema: "documents",
                table: "employee_profile_photos");

            migrationBuilder.DropColumn(
                name: "scan_attempt_count",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "scan_completed_at",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "scan_failure_reason",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "scan_status",
                schema: "documents",
                table: "documents");
        }
    }
}
