using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.CompanyOnboarding.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "company_onboarding");

            migrationBuilder.CreateTable(
                name: "progress",
                schema: "company_onboarding",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_dismissed_early = table.Column<bool>(type: "boolean", nullable: false),
                    is_hidden = table.Column<bool>(type: "boolean", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_progress", x => x.company_id);
                });

            migrationBuilder.CreateTable(
                name: "task_completions",
                schema: "company_onboarding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_completed = table.Column<bool>(type: "boolean", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_completions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_completions_company_id",
                schema: "company_onboarding",
                table: "task_completions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_task_completions_company_id_task_key",
                schema: "company_onboarding",
                table: "task_completions",
                columns: new[] { "company_id", "task_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "progress",
                schema: "company_onboarding");

            migrationBuilder.DropTable(
                name: "task_completions",
                schema: "company_onboarding");
        }
    }
}
