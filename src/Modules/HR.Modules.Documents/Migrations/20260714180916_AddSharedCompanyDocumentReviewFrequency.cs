using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedCompanyDocumentReviewFrequency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "custom_review_frequency_months",
                schema: "documents",
                table: "shared_company_documents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_frequency",
                schema: "documents",
                table: "shared_company_documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "custom_review_frequency_months",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.DropColumn(
                name: "review_frequency",
                schema: "documents",
                table: "shared_company_documents");
        }
    }
}
