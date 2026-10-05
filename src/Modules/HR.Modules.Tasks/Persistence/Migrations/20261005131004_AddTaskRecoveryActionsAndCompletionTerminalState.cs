using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Tasks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskRecoveryActionsAndCompletionTerminalState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "failure_category",
                schema: "tasks",
                table: "task_completion_operations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_reset_at",
                schema: "tasks",
                table: "task_completion_operations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "last_reset_by",
                schema: "tasks",
                table: "task_completion_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "notification_required",
                schema: "tasks",
                table: "task_completion_operations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "previous_task_status",
                schema: "tasks",
                table: "task_completion_operations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reset_count",
                schema: "tasks",
                table: "task_completion_operations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "snapshot_assigned_employee_id",
                schema: "tasks",
                table: "task_completion_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "snapshot_captured_at",
                schema: "tasks",
                table: "task_completion_operations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "snapshot_task_description",
                schema: "tasks",
                table: "task_completion_operations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "snapshot_task_title",
                schema: "tasks",
                table: "task_completion_operations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "task_completed_at",
                schema: "tasks",
                table: "task_completion_operations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "terminal_failure_at",
                schema: "tasks",
                table: "task_completion_operations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "task_recovery_actions",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    operator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    audit_delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    audit_attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_audit_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_audit_failure = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    claimed_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_recovery_actions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_recovery_actions_company_id_task_id",
                schema: "tasks",
                table: "task_recovery_actions",
                columns: new[] { "company_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_recovery_actions_operation_sequence",
                schema: "tasks",
                table: "task_recovery_actions",
                columns: new[] { "operation_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_recovery_actions_undelivered",
                schema: "tasks",
                table: "task_recovery_actions",
                column: "occurred_at",
                filter: "audit_delivered_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_recovery_actions",
                schema: "tasks");

            migrationBuilder.DropColumn(
                name: "failure_category",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "last_reset_at",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "last_reset_by",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "notification_required",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "previous_task_status",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "reset_count",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "snapshot_assigned_employee_id",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "snapshot_captured_at",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "snapshot_task_description",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "snapshot_task_title",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "task_completed_at",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "terminal_failure_at",
                schema: "tasks",
                table: "task_completion_operations");
        }
    }
}
