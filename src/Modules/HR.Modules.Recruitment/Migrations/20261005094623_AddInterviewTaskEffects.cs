using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddInterviewTaskEffects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "interview_task_effects",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interview_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheduled_by = table.Column<Guid>(type: "uuid", nullable: false),
                    interviewer_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    candidate_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    vacancy_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interview_task_effects", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_interview_task_effects_company_id_application_id",
                schema: "recruitment",
                table: "interview_task_effects",
                columns: new[] { "company_id", "application_id" });

            migrationBuilder.CreateIndex(
                name: "IX_interview_task_effects_interview_id",
                schema: "recruitment",
                table: "interview_task_effects",
                column: "interview_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_interview_task_effects_outstanding",
                schema: "recruitment",
                table: "interview_task_effects",
                column: "created_at",
                filter: "completed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "interview_task_effects",
                schema: "recruitment");
        }
    }
}
