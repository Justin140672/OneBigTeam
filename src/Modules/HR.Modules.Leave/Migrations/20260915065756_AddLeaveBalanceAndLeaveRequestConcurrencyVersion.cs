using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Leave.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveBalanceAndLeaveRequestConcurrencyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "leave",
                table: "leave_requests",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "leave",
                table: "leave_balances",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                schema: "leave",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "leave",
                table: "leave_balances");
        }
    }
}
