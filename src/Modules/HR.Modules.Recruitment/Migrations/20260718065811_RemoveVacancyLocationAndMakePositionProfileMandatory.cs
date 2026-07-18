using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class RemoveVacancyLocationAndMakePositionProfileMandatory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "location",
                schema: "recruitment",
                table: "vacancies");

            // Deliberately no defaultValue here (the scaffolded migration proposed silently zero-filling
            // any remaining NULL position_profile_id rows with an empty Guid) — if any legacy vacancy
            // somehow still has a null position_profile_id when this migration runs, the ALTER should
            // fail loudly so it can be triaged (e.g. via the still-present, if now legacy-only,
            // GetVacanciesNeedingPositionProfileReview/ApplyPositionProfileMatches/
            // AssignVacancyPositionProfile admin tooling) rather than silently corrupting data with a
            // fake all-zero Guid. This codebase's own seed data has been verified to contain zero null
            // rows, so this ALTER is expected to succeed as-is in every environment seeded by this repo.
            migrationBuilder.AlterColumn<Guid>(
                name: "position_profile_id",
                schema: "recruitment",
                table: "vacancies",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "position_profile_id",
                schema: "recruitment",
                table: "vacancies",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "location",
                schema: "recruitment",
                table: "vacancies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }
    }
}
