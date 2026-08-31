using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddRecruitmentStagePurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "purpose",
                schema: "recruitment",
                table: "recruitment_stages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_recruitment_stages_company_id_purpose",
                schema: "recruitment",
                table: "recruitment_stages",
                columns: new[] { "company_id", "purpose" });

            // DSH-04: back-fill the new explicit purpose for companies already on the default seeded
            // pipeline (RecruitmentStageSeeder.BuildDefaultStages). Matched by the well-known seed
            // names on non-terminal stages only; fully customised pipelines are left with NULL purpose
            // and are expected to set it from the recruitment stage settings screen.
            migrationBuilder.Sql(
                "UPDATE recruitment.recruitment_stages SET purpose = 'NewApplication' " +
                "WHERE is_terminal = false AND purpose IS NULL AND name = 'Application Received';");
            migrationBuilder.Sql(
                "UPDATE recruitment.recruitment_stages SET purpose = 'Interview' " +
                "WHERE is_terminal = false AND purpose IS NULL AND name = 'Interview';");
            migrationBuilder.Sql(
                "UPDATE recruitment.recruitment_stages SET purpose = 'Offer' " +
                "WHERE is_terminal = false AND purpose IS NULL AND name = 'Offer';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_recruitment_stages_company_id_purpose",
                schema: "recruitment",
                table: "recruitment_stages");

            migrationBuilder.DropColumn(
                name: "purpose",
                schema: "recruitment",
                table: "recruitment_stages");
        }
    }
}
