using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Tasks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProgrammaticTaskCompletionResetTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_reset_at",
                schema: "tasks",
                table: "programmatic_task_completions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "last_reset_by",
                schema: "tasks",
                table: "programmatic_task_completions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reset_count",
                schema: "tasks",
                table: "programmatic_task_completions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_reset_at",
                schema: "tasks",
                table: "programmatic_task_completions");

            migrationBuilder.DropColumn(
                name: "last_reset_by",
                schema: "tasks",
                table: "programmatic_task_completions");

            migrationBuilder.DropColumn(
                name: "reset_count",
                schema: "tasks",
                table: "programmatic_task_completions");
        }
    }
}
