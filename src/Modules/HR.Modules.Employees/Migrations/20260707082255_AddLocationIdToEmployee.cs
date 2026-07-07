using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationIdToEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "location_id",
                schema: "employees",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_employees_location_id",
                schema: "employees",
                table: "employees",
                column: "location_id");

            migrationBuilder.AddForeignKey(
                name: "FK_employees_locations_location_id",
                schema: "employees",
                table: "employees",
                column: "location_id",
                principalSchema: "employees",
                principalTable: "locations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_employees_locations_location_id",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "IX_employees_location_id",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "location_id",
                schema: "employees",
                table: "employees");
        }
    }
}
