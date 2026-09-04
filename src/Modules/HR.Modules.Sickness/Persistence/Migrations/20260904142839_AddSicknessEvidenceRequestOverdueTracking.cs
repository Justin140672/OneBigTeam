using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Sickness.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSicknessEvidenceRequestOverdueTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "overdue_event_published_at",
                schema: "sickness",
                table: "sickness_evidence_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "overdue_notified_at",
                schema: "sickness",
                table: "sickness_evidence_requests",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "overdue_event_published_at",
                schema: "sickness",
                table: "sickness_evidence_requests");

            migrationBuilder.DropColumn(
                name: "overdue_notified_at",
                schema: "sickness",
                table: "sickness_evidence_requests");
        }
    }
}
