using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Tasks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCompletionOperationClaimLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "claimed_by",
                schema: "tasks",
                table: "task_completion_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_expires_at",
                schema: "tasks",
                table: "task_completion_operations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "tasks",
                table: "task_completion_operations",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "claimed_by",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "lease_expires_at",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "tasks",
                table: "task_completion_operations");
        }
    }
}
