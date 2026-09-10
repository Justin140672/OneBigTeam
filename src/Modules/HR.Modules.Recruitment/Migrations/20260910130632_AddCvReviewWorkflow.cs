using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddCvReviewWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "recruitment",
                table: "candidate_documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Other");

            migrationBuilder.AddColumn<string>(
                name: "cv_review_notes",
                schema: "recruitment",
                table: "applications",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cv_reviewed_at",
                schema: "recruitment",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cv_reviewed_by_user_id",
                schema: "recruitment",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_candidate_documents_candidate_id_kind",
                schema: "recruitment",
                table: "candidate_documents",
                columns: new[] { "candidate_id", "kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_candidate_documents_candidate_id_kind",
                schema: "recruitment",
                table: "candidate_documents");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "recruitment",
                table: "candidate_documents");

            migrationBuilder.DropColumn(
                name: "cv_review_notes",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "cv_reviewed_at",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "cv_reviewed_by_user_id",
                schema: "recruitment",
                table: "applications");
        }
    }
}
