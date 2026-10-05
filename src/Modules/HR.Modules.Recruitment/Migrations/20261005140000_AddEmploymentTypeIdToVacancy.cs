using System;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    // Server-only/direct PostgreSQL: adds one nullable column to an existing table; no Supabase Data
    // API grants. Existing vacancies are deliberately not backfilled.
    [DbContext(typeof(RecruitmentDbContext))]
    [Migration("20261005140000_AddEmploymentTypeIdToVacancy")]
    public partial class AddEmploymentTypeIdToVacancy : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "employment_type_id",
                schema: "recruitment",
                table: "vacancies",
                type: "uuid",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "employment_type_id",
                schema: "recruitment",
                table: "vacancies");
        }
    }
}
