using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Support.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportRequestConcurrencyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "support",
                table: "support_requests",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                schema: "support",
                table: "support_requests");
        }
    }
}
