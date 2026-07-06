using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionProfileRequiredAsset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "position_profile_required_assets",
                schema: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_position_profile_required_assets", x => x.id);
                    table.ForeignKey(
                        name: "FK_position_profile_required_assets_position_profiles_position~",
                        column: x => x.position_profile_id,
                        principalSchema: "employees",
                        principalTable: "position_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_position_profile_required_assets_asset_category_id",
                schema: "employees",
                table: "position_profile_required_assets",
                column: "asset_category_id");

            migrationBuilder.CreateIndex(
                name: "IX_position_profile_required_assets_company_id",
                schema: "employees",
                table: "position_profile_required_assets",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_position_profile_required_assets_position_profile_id",
                schema: "employees",
                table: "position_profile_required_assets",
                column: "position_profile_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "position_profile_required_assets",
                schema: "employees");
        }
    }
}
