using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddInterviewOutcomeReconciliationBlockedState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_interview_outcome_task_reconciliations_outstanding",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "blocked_at",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "blocked_category",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "blocked_task_id",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "blocked_tasks_operation_id",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_repaired_at",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "last_repaired_by",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "repair_count",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_interview_outcome_task_reconciliations_blocked",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations",
                column: "blocked_at",
                filter: "blocked_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_interview_outcome_task_reconciliations_outstanding",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations",
                column: "created_at",
                filter: "completed_at IS NULL AND blocked_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_interview_outcome_task_reconciliations_blocked",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations");

            migrationBuilder.DropIndex(
                name: "ix_interview_outcome_task_reconciliations_outstanding",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations");

            migrationBuilder.DropColumn(
                name: "blocked_at",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations");

            migrationBuilder.DropColumn(
                name: "blocked_category",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations");

            migrationBuilder.DropColumn(
                name: "blocked_task_id",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations");

            migrationBuilder.DropColumn(
                name: "blocked_tasks_operation_id",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations");

            migrationBuilder.DropColumn(
                name: "last_repaired_at",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations");

            migrationBuilder.DropColumn(
                name: "last_repaired_by",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations");

            migrationBuilder.DropColumn(
                name: "repair_count",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations");

            migrationBuilder.CreateIndex(
                name: "ix_interview_outcome_task_reconciliations_outstanding",
                schema: "recruitment",
                table: "interview_outcome_task_reconciliations",
                column: "created_at",
                filter: "completed_at IS NULL");
        }
    }
}
