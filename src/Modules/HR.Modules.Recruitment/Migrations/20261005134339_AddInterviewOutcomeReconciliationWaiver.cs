using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddInterviewOutcomeReconciliationWaiver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "waived_at",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "waived_tasks_operation_id",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "waived_at",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations");

            migrationBuilder.DropColumn(
                name: "waived_tasks_operation_id",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations");
        }
    }
}
