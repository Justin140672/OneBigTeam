using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Tasks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCompletionAdjudication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_task_recovery_actions_operation_sequence",
                schema: "tasks",
                table: "task_recovery_actions");

            migrationBuilder.AddColumn<string>(
                name: "action_type",
                schema: "tasks",
                table: "task_recovery_actions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "reset");

            migrationBuilder.AddColumn<bool>(
                name: "evidence_supplied",
                schema: "tasks",
                table: "task_recovery_actions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "previous_status",
                schema: "tasks",
                table: "task_recovery_actions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution_type",
                schema: "tasks",
                table: "task_recovery_actions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resulting_status",
                schema: "tasks",
                table: "task_recovery_actions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "adjudication_count",
                schema: "tasks",
                table: "task_completion_operations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_adjudicated_at",
                schema: "tasks",
                table: "task_completion_operations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "last_adjudicated_by",
                schema: "tasks",
                table: "task_completion_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution_type",
                schema: "tasks",
                table: "task_completion_operations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resolved_at",
                schema: "tasks",
                table: "task_completion_operations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_recovery_actions_operation_sequence",
                schema: "tasks",
                table: "task_recovery_actions",
                columns: new[] { "operation_id", "action_type", "sequence_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_task_recovery_actions_operation_sequence",
                schema: "tasks",
                table: "task_recovery_actions");

            migrationBuilder.DropColumn(
                name: "action_type",
                schema: "tasks",
                table: "task_recovery_actions");

            migrationBuilder.DropColumn(
                name: "evidence_supplied",
                schema: "tasks",
                table: "task_recovery_actions");

            migrationBuilder.DropColumn(
                name: "previous_status",
                schema: "tasks",
                table: "task_recovery_actions");

            migrationBuilder.DropColumn(
                name: "resolution_type",
                schema: "tasks",
                table: "task_recovery_actions");

            migrationBuilder.DropColumn(
                name: "resulting_status",
                schema: "tasks",
                table: "task_recovery_actions");

            migrationBuilder.DropColumn(
                name: "adjudication_count",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "last_adjudicated_at",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "last_adjudicated_by",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "resolution_type",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "resolved_at",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.CreateIndex(
                name: "ix_task_recovery_actions_operation_sequence",
                schema: "tasks",
                table: "task_recovery_actions",
                columns: new[] { "operation_id", "sequence_number" },
                unique: true);
        }
    }
}
