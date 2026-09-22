using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Tasks.Migrations
{
    /// <inheritdoc />
    public partial class Ticket23_AddTaskCompletionOperationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "causation_id",
                schema: "tasks",
                table: "task_completion_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                schema: "tasks",
                table: "task_completion_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "message_id",
                schema: "tasks",
                table: "task_completion_operations",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "causation_id",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "message_id",
                schema: "tasks",
                table: "task_completion_operations");
        }
    }
}
