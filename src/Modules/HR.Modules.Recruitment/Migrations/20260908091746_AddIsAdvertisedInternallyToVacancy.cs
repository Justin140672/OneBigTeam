using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddIsAdvertisedInternallyToVacancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_advertised_internally",
                schema: "recruitment",
                table: "vacancies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_vacancies_company_id_is_advertised_internally_status",
                schema: "recruitment",
                table: "vacancies",
                columns: new[] { "company_id", "is_advertised_internally", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_vacancies_company_id_is_advertised_internally_status",
                schema: "recruitment",
                table: "vacancies");

            migrationBuilder.DropColumn(
                name: "is_advertised_internally",
                schema: "recruitment",
                table: "vacancies");
        }
    }
}
