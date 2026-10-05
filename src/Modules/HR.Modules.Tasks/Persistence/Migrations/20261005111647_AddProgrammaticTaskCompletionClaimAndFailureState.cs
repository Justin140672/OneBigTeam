using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Tasks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProgrammaticTaskCompletionClaimAndFailureState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                schema: "tasks",
                table: "programmatic_task_completions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "claimed_by",
                schema: "tasks",
                table: "programmatic_task_completions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "claimed_until",
                schema: "tasks",
                table: "programmatic_task_completions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dispatch_mode",
                schema: "tasks",
                table: "programmatic_task_completions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "dispatch");

            migrationBuilder.AddColumn<string>(
                name: "failure_reason",
                schema: "tasks",
                table: "programmatic_task_completions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_attempt_at",
                schema: "tasks",
                table: "programmatic_task_completions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "operation_id",
                schema: "tasks",
                table: "programmatic_task_completions",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "terminal_failure_at",
                schema: "tasks",
                table: "programmatic_task_completions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "tasks",
                table: "programmatic_task_completions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ix_programmatic_task_completions_operation_id",
                schema: "tasks",
                table: "programmatic_task_completions",
                column: "operation_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_programmatic_task_completions_operation_id",
                schema: "tasks",
                table: "programmatic_task_completions");

            migrationBuilder.DropColumn(
                name: "attempt_count",
                schema: "tasks",
                table: "programmatic_task_completions");

            migrationBuilder.DropColumn(
                name: "claimed_by",
                schema: "tasks",
                table: "programmatic_task_completions");

            migrationBuilder.DropColumn(
                name: "claimed_until",
                schema: "tasks",
                table: "programmatic_task_completions");

            migrationBuilder.DropColumn(
                name: "dispatch_mode",
                schema: "tasks",
                table: "programmatic_task_completions");

            migrationBuilder.DropColumn(
                name: "failure_reason",
                schema: "tasks",
                table: "programmatic_task_completions");

            migrationBuilder.DropColumn(
                name: "last_attempt_at",
                schema: "tasks",
                table: "programmatic_task_completions");

            migrationBuilder.DropColumn(
                name: "operation_id",
                schema: "tasks",
                table: "programmatic_task_completions");

            migrationBuilder.DropColumn(
                name: "terminal_failure_at",
                schema: "tasks",
                table: "programmatic_task_completions");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "tasks",
                table: "programmatic_task_completions");
        }
    }
}
