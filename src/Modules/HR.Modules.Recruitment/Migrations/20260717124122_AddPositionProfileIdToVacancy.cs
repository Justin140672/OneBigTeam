using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionProfileIdToVacancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable at the DB level intentionally: "vacancies" is an existing, populated table and
            // there is no sensible position profile to backfill onto pre-existing rows. New vacancies
            // are required to supply a PositionProfileId at the domain/API layer (see Vacancy.Create
            // and CreateVacancyValidator); only legacy rows created before this migration read back
            // as null.
            migrationBuilder.AddColumn<Guid>(
                name: "position_profile_id",
                schema: "recruitment",
                table: "vacancies",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_vacancies_position_profile_id",
                schema: "recruitment",
                table: "vacancies",
                column: "position_profile_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_vacancies_position_profile_id",
                schema: "recruitment",
                table: "vacancies");

            migrationBuilder.DropColumn(
                name: "position_profile_id",
                schema: "recruitment",
                table: "vacancies");
        }
    }
}
