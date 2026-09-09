using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddRecruitmentConcurrencyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "recruitment",
                table: "vacancies",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "recruitment",
                table: "recruitment_stages",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "recruitment",
                table: "interviews",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "recruitment",
                table: "external_recruiters",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "recruitment",
                table: "candidates",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                schema: "recruitment",
                table: "vacancies");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "recruitment",
                table: "recruitment_stages");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "recruitment",
                table: "interviews");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "recruitment",
                table: "external_recruiters");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "recruitment",
                table: "candidates");
        }
    }
}
