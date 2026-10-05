using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddInterviewOutcomeTaskReconciliations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "interview_outcome_task_reconciliations",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interview_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claimed_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interview_outcome_task_reconciliations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_interview_outcome_task_reconciliations_company_id_applicati~",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations",
                columns: new[] { "company_id", "application_id" });

            migrationBuilder.CreateIndex(
                name: "IX_interview_outcome_task_reconciliations_interview_id",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations",
                column: "interview_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_interview_outcome_task_reconciliations_outstanding",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations",
                column: "created_at",
                filter: "completed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "interview_outcome_task_reconciliations",
                schema: "recruitment");
        }
    }
}
