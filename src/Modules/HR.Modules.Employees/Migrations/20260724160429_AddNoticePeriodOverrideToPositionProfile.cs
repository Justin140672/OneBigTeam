using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddNoticePeriodOverrideToPositionProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "notice_period_length_override",
                schema: "employees",
                table: "position_profiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notice_period_unit_override",
                schema: "employees",
                table: "position_profiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "notice_period_length_override",
                schema: "employees",
                table: "position_profiles");

            migrationBuilder.DropColumn(
                name: "notice_period_unit_override",
                schema: "employees",
                table: "position_profiles");
        }
    }
}
