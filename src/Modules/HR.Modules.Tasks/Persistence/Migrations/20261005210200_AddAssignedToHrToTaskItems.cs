using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Tasks.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignedToHrToTaskItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "assigned_to_hr",
                schema: "tasks",
                table: "task_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "assigned_to_hr",
                schema: "tasks",
                table: "task_items");
        }
    }
}
