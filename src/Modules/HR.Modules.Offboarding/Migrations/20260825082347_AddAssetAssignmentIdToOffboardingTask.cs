using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Offboarding.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetAssignmentIdToOffboardingTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "asset_assignment_id",
                schema: "offboarding",
                table: "offboarding_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_tasks_asset_assignment_id",
                schema: "offboarding",
                table: "offboarding_tasks",
                column: "asset_assignment_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_offboarding_tasks_asset_assignment_id",
                schema: "offboarding",
                table: "offboarding_tasks");

            migrationBuilder.DropColumn(
                name: "asset_assignment_id",
                schema: "offboarding",
                table: "offboarding_tasks");
        }
    }
}
