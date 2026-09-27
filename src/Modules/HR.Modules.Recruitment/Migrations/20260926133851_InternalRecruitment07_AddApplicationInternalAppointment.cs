using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class InternalRecruitment07_AddApplicationInternalAppointment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Internal recruitment Ticket 7. Supabase Data API classification: server-only/direct
            // PostgreSQL. Adds nullable columns and a partial index to the existing
            // recruitment.applications table only; no grants to anon/authenticated/service_role.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "appointment_completed_at",
                schema: "recruitment",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "appointment_effective_date",
                schema: "recruitment",
                table: "applications",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "appointment_employee_id",
                schema: "recruitment",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "appointment_promotion_id",
                schema: "recruitment",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "appointment_requested_at",
                schema: "recruitment",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "appointment_requested_by_user_id",
                schema: "recruitment",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "appointment_status",
                schema: "recruitment",
                table: "applications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_applications_appointment_pending",
                schema: "recruitment",
                table: "applications",
                columns: new[] { "appointment_status", "appointment_requested_at" },
                filter: "appointment_status = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_applications_appointment_pending",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "appointment_completed_at",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "appointment_effective_date",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "appointment_employee_id",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "appointment_promotion_id",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "appointment_requested_at",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "appointment_requested_by_user_id",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "appointment_status",
                schema: "recruitment",
                table: "applications");
        }
    }
}
