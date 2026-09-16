using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Tasks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskCompletionOperationsActiveUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_task_completion_operations_task_id",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.CreateIndex(
                name: "ix_task_completion_operations_task_id_active",
                schema: "tasks",
                table: "task_completion_operations",
                column: "task_id",
                unique: true,
                filter: "status <> 'rejected'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_task_completion_operations_task_id_active",
                schema: "tasks",
                table: "task_completion_operations");

            migrationBuilder.CreateIndex(
                name: "IX_task_completion_operations_task_id",
                schema: "tasks",
                table: "task_completion_operations",
                column: "task_id");
        }
    }
}
