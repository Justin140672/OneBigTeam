using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class RenameVacancyAdvertFieldsAndDropDepartmentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_vacancies_department_id",
                schema: "recruitment",
                table: "vacancies");

            migrationBuilder.DropColumn(
                name: "department_id",
                schema: "recruitment",
                table: "vacancies");

            // Renamed (not dropped/re-added) so existing vacancy data is preserved — the default
            // scaffolded migration used DropColumn+AddColumn here because the property also became
            // nullable, which this hand-edit corrects: RenameColumn first, then relax the NOT NULL
            // constraint on the (already-populated) renamed column.
            migrationBuilder.RenameColumn(
                name: "title",
                schema: "recruitment",
                table: "vacancies",
                newName: "advert_title");

            migrationBuilder.AlterColumn<string>(
                name: "advert_title",
                schema: "recruitment",
                table: "vacancies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.RenameColumn(
                name: "description",
                schema: "recruitment",
                table: "vacancies",
                newName: "advert_description");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "advert_description",
                schema: "recruitment",
                table: "vacancies",
                newName: "description");

            migrationBuilder.AlterColumn<string>(
                name: "advert_title",
                schema: "recruitment",
                table: "vacancies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "advert_title",
                schema: "recruitment",
                table: "vacancies",
                newName: "title");

            migrationBuilder.AddColumn<Guid>(
                name: "department_id",
                schema: "recruitment",
                table: "vacancies",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_vacancies_department_id",
                schema: "recruitment",
                table: "vacancies",
                column: "department_id");
        }
    }
}
