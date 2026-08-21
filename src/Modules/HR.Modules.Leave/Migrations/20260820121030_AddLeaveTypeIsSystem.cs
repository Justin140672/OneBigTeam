using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Leave.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveTypeIsSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_system",
                schema: "leave",
                table: "leave_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_system",
                schema: "leave",
                table: "leave_types");
        }
    }
}
