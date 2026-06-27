using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Tasks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActionTypeToTaskItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "action_type",
                schema: "tasks",
                table: "task_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Complete");

            migrationBuilder.Sql("""
                UPDATE tasks.task_items SET action_type = 'Approve' WHERE source = 'Leave';
                UPDATE tasks.task_items SET action_type = 'Upload'  WHERE source = 'Document';
                UPDATE tasks.task_items SET action_type = 'Review',
                                            source      = 'Probation' WHERE source = 'ProbationReview';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "action_type",
                schema: "tasks",
                table: "task_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: false,
                oldDefaultValue: "Complete");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "action_type",
                schema: "tasks",
                table: "task_items");
        }
    }
}
