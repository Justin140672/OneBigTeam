using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddInterviewOutcomeRepairActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "interview_outcome_repair_actions",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reconciliation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interview_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tasks_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    blocked_category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    operator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    audit_delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    audit_attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_audit_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_audit_failure = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    claimed_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interview_outcome_repair_actions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_interview_outcome_repair_actions_company_id_interview_id",
                schema: "recruitment",
                table: "interview_outcome_repair_actions",
                columns: new[] { "company_id", "interview_id" });

            migrationBuilder.CreateIndex(
                name: "ix_interview_outcome_repair_actions_reconciliation_sequence",
                schema: "recruitment",
                table: "interview_outcome_repair_actions",
                columns: new[] { "reconciliation_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_interview_outcome_repair_actions_undelivered",
                schema: "recruitment",
                table: "interview_outcome_repair_actions",
                column: "occurred_at",
                filter: "audit_delivered_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "interview_outcome_repair_actions",
                schema: "recruitment");
        }
    }
}
