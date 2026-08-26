using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddOverrideReasonExpiryAndCompanyId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_employee_role_overrides_users_user_id",
                schema: "identity",
                table: "employee_role_overrides");

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "identity",
                table: "employee_role_overrides",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                schema: "identity",
                table: "employee_role_overrides",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason",
                schema: "identity",
                table: "employee_role_overrides",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_employee_role_overrides_company_id",
                schema: "identity",
                table: "employee_role_overrides",
                column: "company_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_employee_role_overrides_company_id",
                schema: "identity",
                table: "employee_role_overrides");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "identity",
                table: "employee_role_overrides");

            migrationBuilder.DropColumn(
                name: "expires_at",
                schema: "identity",
                table: "employee_role_overrides");

            migrationBuilder.DropColumn(
                name: "reason",
                schema: "identity",
                table: "employee_role_overrides");

            migrationBuilder.AddForeignKey(
                name: "FK_employee_role_overrides_users_user_id",
                schema: "identity",
                table: "employee_role_overrides",
                column: "user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
