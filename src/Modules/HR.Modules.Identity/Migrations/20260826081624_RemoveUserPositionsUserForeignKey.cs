using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUserPositionsUserForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_user_positions_users_user_id",
                schema: "identity",
                table: "user_positions");

            migrationBuilder.CreateIndex(
                name: "IX_user_positions_user_id",
                schema: "identity",
                table: "user_positions",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_positions_user_id",
                schema: "identity",
                table: "user_positions");

            migrationBuilder.AddForeignKey(
                name: "FK_user_positions_users_user_id",
                schema: "identity",
                table: "user_positions",
                column: "user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
