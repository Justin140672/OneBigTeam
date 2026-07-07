using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationIdToPositionProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "location_id",
                schema: "employees",
                table: "position_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_position_profiles_location_id",
                schema: "employees",
                table: "position_profiles",
                column: "location_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_position_profiles_location_id",
                schema: "employees",
                table: "position_profiles");

            migrationBuilder.DropColumn(
                name: "location_id",
                schema: "employees",
                table: "position_profiles");
        }
    }
}
