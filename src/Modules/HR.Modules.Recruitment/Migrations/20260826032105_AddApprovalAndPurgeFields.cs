using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalAndPurgeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                schema: "recruitment",
                table: "vacancies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by_user_id",
                schema: "recruitment",
                table: "vacancies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "purged_at",
                schema: "recruitment",
                table: "candidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "purged_by_user_id",
                schema: "recruitment",
                table: "candidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "offer_approved_at",
                schema: "recruitment",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "offer_approved_by_user_id",
                schema: "recruitment",
                table: "applications",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "recruitment",
                table: "vacancies");

            migrationBuilder.DropColumn(
                name: "approved_by_user_id",
                schema: "recruitment",
                table: "vacancies");

            migrationBuilder.DropColumn(
                name: "purged_at",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "purged_by_user_id",
                schema: "recruitment",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "offer_approved_at",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_approved_by_user_id",
                schema: "recruitment",
                table: "applications");
        }
    }
}
