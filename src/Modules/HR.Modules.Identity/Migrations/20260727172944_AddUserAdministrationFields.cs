using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAdministrationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_login_at",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                schema: "identity",
                table: "user_invites",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                schema: "identity",
                table: "user_invites",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_role_ids",
                schema: "identity",
                table: "user_invites",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_login_at",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "identity",
                table: "user_invites");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                schema: "identity",
                table: "user_invites");

            migrationBuilder.DropColumn(
                name: "pending_role_ids",
                schema: "identity",
                table: "user_invites");
        }
    }
}
