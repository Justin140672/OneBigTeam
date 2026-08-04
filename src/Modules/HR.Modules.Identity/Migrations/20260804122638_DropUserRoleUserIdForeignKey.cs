using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class DropUserRoleUserIdForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_user_roles_users_user_id",
                schema: "identity",
                table: "user_roles");

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_user_id",
                schema: "identity",
                table: "user_roles",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_roles_user_id",
                schema: "identity",
                table: "user_roles");

            migrationBuilder.AddForeignKey(
                name: "FK_user_roles_users_user_id",
                schema: "identity",
                table: "user_roles",
                column: "user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
