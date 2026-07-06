using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "recruitment");

            migrationBuilder.CreateTable(
                name: "candidates",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    resume_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_candidates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vacancies",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    hiring_manager_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_at = table.Column<DateOnly>(type: "date", nullable: true),
                    closed_at = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vacancies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "applications",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vacancy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    interview_outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applications", x => x.id);
                    table.ForeignKey(
                        name: "FK_applications_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalSchema: "recruitment",
                        principalTable: "candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_applications_vacancies_vacancy_id",
                        column: x => x.vacancy_id,
                        principalSchema: "recruitment",
                        principalTable: "vacancies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_applications_candidate_id",
                schema: "recruitment",
                table: "applications",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "IX_applications_company_id",
                schema: "recruitment",
                table: "applications",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_applications_vacancy_id",
                schema: "recruitment",
                table: "applications",
                column: "vacancy_id");

            migrationBuilder.CreateIndex(
                name: "IX_applications_vacancy_id_candidate_id",
                schema: "recruitment",
                table: "applications",
                columns: new[] { "vacancy_id", "candidate_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_candidates_company_id",
                schema: "recruitment",
                table: "candidates",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_candidates_company_id_email",
                schema: "recruitment",
                table: "candidates",
                columns: new[] { "company_id", "email" });

            migrationBuilder.CreateIndex(
                name: "IX_vacancies_company_id",
                schema: "recruitment",
                table: "vacancies",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_vacancies_company_id_status",
                schema: "recruitment",
                table: "vacancies",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_vacancies_department_id",
                schema: "recruitment",
                table: "vacancies",
                column: "department_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "applications",
                schema: "recruitment");

            migrationBuilder.DropTable(
                name: "candidates",
                schema: "recruitment");

            migrationBuilder.DropTable(
                name: "vacancies",
                schema: "recruitment");
        }
    }
}
