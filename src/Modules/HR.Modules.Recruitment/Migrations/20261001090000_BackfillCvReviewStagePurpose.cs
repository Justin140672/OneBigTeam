using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class BackfillCvReviewStagePurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE recruitment.recruitment_stages SET purpose = 'CvReview' " +
                "WHERE is_terminal = false AND purpose IS NULL AND name = 'CV Review' " +
                "AND NOT EXISTS (SELECT 1 FROM recruitment.recruitment_stages x " +
                "WHERE x.company_id = recruitment_stages.company_id AND x.purpose = 'CvReview');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE recruitment.recruitment_stages SET purpose = NULL " +
                "WHERE purpose = 'CvReview' AND name = 'CV Review';");
        }
    }
}
