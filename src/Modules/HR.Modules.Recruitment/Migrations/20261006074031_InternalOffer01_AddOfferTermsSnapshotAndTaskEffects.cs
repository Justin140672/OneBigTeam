using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class InternalOffer01_AddOfferTermsSnapshotAndTaskEffects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "offer_currency",
                schema: "recruitment",
                table: "applications",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "offer_department_id",
                schema: "recruitment",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "offer_department_name",
                schema: "recruitment",
                table: "applications",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "offer_employment_type_id",
                schema: "recruitment",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "offer_employment_type_name",
                schema: "recruitment",
                table: "applications",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "offer_fte",
                schema: "recruitment",
                table: "applications",
                type: "numeric(4,3)",
                precision: 4,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "offer_hours_per_day",
                schema: "recruitment",
                table: "applications",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "offer_hours_per_week",
                schema: "recruitment",
                table: "applications",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "offer_job_title",
                schema: "recruitment",
                table: "applications",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "offer_location_id",
                schema: "recruitment",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "offer_location_name",
                schema: "recruitment",
                table: "applications",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "offer_made_by_user_id",
                schema: "recruitment",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "offer_no_manager",
                schema: "recruitment",
                table: "applications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "offer_position_profile_id",
                schema: "recruitment",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "offer_probation_months",
                schema: "recruitment",
                table: "applications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "offer_proposed_manager_id",
                schema: "recruitment",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "offer_proposed_manager_name",
                schema: "recruitment",
                table: "applications",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "offer_responded_by_user_id",
                schema: "recruitment",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "offer_response_channel",
                schema: "recruitment",
                table: "applications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "offer_response_deadline",
                schema: "recruitment",
                table: "applications",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "offer_response_reason",
                schema: "recruitment",
                table: "applications",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "offer_terms_snapshot_at",
                schema: "recruitment",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "offer_version",
                schema: "recruitment",
                table: "applications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "offer_working_days",
                schema: "recruitment",
                table: "applications",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "internal_offer_task_effects",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_version = table.Column<int>(type: "integer", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    made_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    response_deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    task_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    response_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_internal_offer_task_effects", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_internal_offer_task_effects_application_id_offer_version",
                schema: "recruitment",
                table: "internal_offer_task_effects",
                columns: new[] { "application_id", "offer_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_internal_offer_task_effects_company_id_application_id",
                schema: "recruitment",
                table: "internal_offer_task_effects",
                columns: new[] { "company_id", "application_id" });

            migrationBuilder.CreateIndex(
                name: "ix_internal_offer_task_effects_open",
                schema: "recruitment",
                table: "internal_offer_task_effects",
                column: "created_at",
                filter: "closed_at IS NULL");

            migrationBuilder.Sql(
                "UPDATE recruitment.applications SET offer_version = 1 WHERE offer_response_status IS NOT NULL AND offer_version = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "internal_offer_task_effects",
                schema: "recruitment");

            migrationBuilder.DropColumn(
                name: "offer_currency",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_department_id",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_department_name",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_employment_type_id",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_employment_type_name",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_fte",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_hours_per_day",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_hours_per_week",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_job_title",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_location_id",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_location_name",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_made_by_user_id",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_no_manager",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_position_profile_id",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_probation_months",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_proposed_manager_id",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_proposed_manager_name",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_responded_by_user_id",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_response_channel",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_response_deadline",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_response_reason",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_terms_snapshot_at",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_version",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_working_days",
                schema: "recruitment",
                table: "applications");
        }
    }
}
