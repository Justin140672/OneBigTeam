using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AllowDuplicatePositionNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_positions_tenant_id_normalized_name",
                schema: "identity",
                table: "positions");

            migrationBuilder.CreateIndex(
                name: "IX_positions_tenant_id_normalized_name",
                schema: "identity",
                table: "positions",
                columns: new[] { "tenant_id", "normalized_name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_positions_tenant_id_normalized_name",
                schema: "identity",
                table: "positions");

            migrationBuilder.CreateIndex(
                name: "IX_positions_tenant_id_normalized_name",
                schema: "identity",
                table: "positions",
                columns: new[] { "tenant_id", "normalized_name" },
                unique: true);
        }
    }
}
