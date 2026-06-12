using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Leave.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveRequestDayParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "notes",
                schema: "leave",
                table: "leave_requests",
                newName: "reason");

            migrationBuilder.AddColumn<string>(
                name: "end_part",
                schema: "leave",
                table: "leave_requests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "start_part",
                schema: "leave",
                table: "leave_requests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "end_part",
                schema: "leave",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "start_part",
                schema: "leave",
                table: "leave_requests");

            migrationBuilder.RenameColumn(
                name: "reason",
                schema: "leave",
                table: "leave_requests",
                newName: "notes");
        }
    }
}
