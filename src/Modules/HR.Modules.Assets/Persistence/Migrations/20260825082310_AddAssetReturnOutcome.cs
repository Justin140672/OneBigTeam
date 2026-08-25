using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Assets.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetReturnOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "return_outcome",
                schema: "assets",
                table: "asset_assignments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "return_outcome",
                schema: "assets",
                table: "asset_assignments");
        }
    }
}
