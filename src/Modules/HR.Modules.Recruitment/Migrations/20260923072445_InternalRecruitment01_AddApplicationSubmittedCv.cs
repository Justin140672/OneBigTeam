using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class InternalRecruitment01_AddApplicationSubmittedCv : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cv_document_id",
                schema: "recruitment",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_candidate_documents_id_candidate_id_company_id",
                schema: "recruitment",
                table: "candidate_documents",
                columns: new[] { "id", "candidate_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_applications_cv_document_id",
                schema: "recruitment",
                table: "applications",
                columns: new[] { "cv_document_id", "candidate_id", "company_id" },
                filter: "cv_document_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_applications_candidate_documents_cv_document",
                schema: "recruitment",
                table: "applications",
                columns: new[] { "cv_document_id", "candidate_id", "company_id" },
                principalSchema: "recruitment",
                principalTable: "candidate_documents",
                principalColumns: new[] { "id", "candidate_id", "company_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_applications_candidate_documents_cv_document",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_candidate_documents_id_candidate_id_company_id",
                schema: "recruitment",
                table: "candidate_documents");

            migrationBuilder.DropIndex(
                name: "ix_applications_cv_document_id",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "cv_document_id",
                schema: "recruitment",
                table: "applications");
        }
    }
}
