using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Leave.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveTypeHasBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_balance",
                schema: "leave",
                table: "leave_types",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "has_balance",
                schema: "leave",
                table: "leave_types");
        }
    }
}
