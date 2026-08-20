using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateDeactivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deactivated_at",
                schema: "recruitment",
                table: "candidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deactivated_by_user_id",
                schema: "recruitment",
                table: "candidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deactivation_reason",
                schema: "recruitment",
                table: "candidates",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "recruitment",
                table: "candidates",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reactivated_at",
                schema: "recruitment",
                table: "candidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reactivated_by_user_id",
                schema: "recruitment",
                table: "candidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_candidates_company_id_is_active",
                schema: "recruitment",
                table: "candidates",
                columns: new[] { "company_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_candidates_company_id_is_active",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "deactivated_at",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "deactivated_by_user_id",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "deactivation_reason",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "reactivated_at",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "reactivated_by_user_id",
                schema: "recruitment",
                table: "candidates");
        }
    }
}
