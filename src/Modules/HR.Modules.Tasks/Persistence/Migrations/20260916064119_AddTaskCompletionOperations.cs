using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Tasks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCompletionOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_completion_operations",
                schema: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome_decision = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    outcome_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_completion_operations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_completion_operations_company_id_status",
                schema: "tasks",
                table: "task_completion_operations",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_task_completion_operations_task_id",
                schema: "tasks",
                table: "task_completion_operations",
                column: "task_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_completion_operations",
                schema: "tasks");
        }
    }
}
