using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalRecruitersAndApplicationSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "recruitment",
                table: "applications",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_external_recruiter_id",
                schema: "recruitment",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "external_recruiters",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agency_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    contact_telephone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    website = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_recruiters", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_vacancies_assigned_recruiter_id",
                schema: "recruitment",
                table: "vacancies",
                column: "assigned_recruiter_id");

            migrationBuilder.CreateIndex(
                name: "IX_applications_source_external_recruiter_id",
                schema: "recruitment",
                table: "applications",
                column: "source_external_recruiter_id");

            migrationBuilder.CreateIndex(
                name: "IX_external_recruiters_company_id",
                schema: "recruitment",
                table: "external_recruiters",
                column: "company_id");

            migrationBuilder.AddForeignKey(
                name: "FK_vacancies_external_recruiters_assigned_recruiter_id",
                schema: "recruitment",
                table: "vacancies",
                column: "assigned_recruiter_id",
                principalSchema: "recruitment",
                principalTable: "external_recruiters",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_vacancies_external_recruiters_assigned_recruiter_id",
                schema: "recruitment",
                table: "vacancies");

            migrationBuilder.DropTable(
                name: "external_recruiters",
                schema: "recruitment");

            migrationBuilder.DropIndex(
                name: "IX_vacancies_assigned_recruiter_id",
                schema: "recruitment",
                table: "vacancies");

            migrationBuilder.DropIndex(
                name: "IX_applications_source_external_recruiter_id",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "source_external_recruiter_id",
                schema: "recruitment",
                table: "applications");
        }
    }
}
