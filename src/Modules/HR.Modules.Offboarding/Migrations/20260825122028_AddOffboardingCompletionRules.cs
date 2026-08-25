using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Offboarding.Migrations
{
    /// <inheritdoc />
    public partial class AddOffboardingCompletionRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // OFF-07: default true — existing tasks predate the mandatory/optional distinction and
            // were, by construction, always material exit obligations (asset returns, document
            // review, manager exit checklist). Defaulting to false would silently make every
            // pre-existing outstanding task optional and able to bypass the new completion gate.
            migrationBuilder.AddColumn<bool>(
                name: "is_mandatory",
                schema: "offboarding",
                table: "offboarding_tasks",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "skip_reason",
                schema: "offboarding",
                table: "offboarding_tasks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "skipped_at",
                schema: "offboarding",
                table: "offboarding_tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "skipped_by_user_id",
                schema: "offboarding",
                table: "offboarding_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "final_review_task_created_at",
                schema: "offboarding",
                table: "offboarding_plans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "has_incomplete_offboarding_at_departure",
                schema: "offboarding",
                table: "offboarding_plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_plans_company_id_incomplete_at_departure",
                schema: "offboarding",
                table: "offboarding_plans",
                columns: new[] { "company_id", "has_incomplete_offboarding_at_departure" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_offboarding_plans_company_id_incomplete_at_departure",
                schema: "offboarding",
                table: "offboarding_plans");

            migrationBuilder.DropColumn(
                name: "is_mandatory",
                schema: "offboarding",
                table: "offboarding_tasks");

            migrationBuilder.DropColumn(
                name: "skip_reason",
                schema: "offboarding",
                table: "offboarding_tasks");

            migrationBuilder.DropColumn(
                name: "skipped_at",
                schema: "offboarding",
                table: "offboarding_tasks");

            migrationBuilder.DropColumn(
                name: "skipped_by_user_id",
                schema: "offboarding",
                table: "offboarding_tasks");

            migrationBuilder.DropColumn(
                name: "final_review_task_created_at",
                schema: "offboarding",
                table: "offboarding_plans");

            migrationBuilder.DropColumn(
                name: "has_incomplete_offboarding_at_departure",
                schema: "offboarding",
                table: "offboarding_plans");
        }
    }
}
