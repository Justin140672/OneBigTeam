using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Tasks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyKeyToTaskItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                schema: "tasks",
                table: "task_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_items_company_id_idempotency_key",
                schema: "tasks",
                table: "task_items",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_task_items_company_id_idempotency_key",
                schema: "tasks",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                schema: "tasks",
                table: "task_items");
        }
    }
}
