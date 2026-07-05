using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionProfileTemplateDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "default_leave_policy_id",
                schema: "employees",
                table: "position_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "hours_per_day_override",
                schema: "employees",
                table: "position_profiles",
                type: "numeric(4,2)",
                precision: 4,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "salary_max",
                schema: "employees",
                table: "position_profiles",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "salary_min",
                schema: "employees",
                table: "position_profiles",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "working_days_override",
                schema: "employees",
                table: "position_profiles",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_leave_policy_id",
                schema: "employees",
                table: "position_profiles");

            migrationBuilder.DropColumn(
                name: "hours_per_day_override",
                schema: "employees",
                table: "position_profiles");

            migrationBuilder.DropColumn(
                name: "salary_max",
                schema: "employees",
                table: "position_profiles");

            migrationBuilder.DropColumn(
                name: "salary_min",
                schema: "employees",
                table: "position_profiles");

            migrationBuilder.DropColumn(
                name: "working_days_override",
                schema: "employees",
                table: "position_profiles");
        }
    }
}
