using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Ticket #99: replaces the fixed ApplicationStatus enum ("status" column) with a per-company
    /// configurable RecruitmentStage table ("current_stage_id" FK), plus a standalone "withdrawn_at"
    /// flag replacing the old ApplicationStatus.Withdrawn value (see Application.WithdrawnAt's
    /// remarks for the judgement call). The generated migration has been hand-edited to reorder the
    /// steps and add a data backfill — EF's scaffolded Up() would otherwise drop the "status" column
    /// before any replacement data existed, silently losing every application's pipeline position.
    ///
    /// Data migration approach (documented judgement call): for every company that already has at
    /// least one Vacancy or Application row, this seeds the same six default stages
    /// RecruitmentStageSeeder produces (Application Received / CV Review / Interview / Offer / Hired
    /// / Rejected), then maps each existing application's old status value onto the matching stage
    /// name:
    ///   Applied            -> Application Received
    ///   Screening          -> CV Review
    ///   InterviewScheduled -> Interview
    ///   Interviewed        -> Interview
    ///   Offered            -> Offer
    ///   Hired              -> Hired (terminal)
    ///   Rejected           -> Rejected (terminal)
    ///   Withdrawn          -> Application Received (see remarks below), with withdrawn_at set to the
    ///                         application's updated_at
    /// For Withdrawn applications the true stage at time of withdrawal is not reconstructed from
    /// ApplicationStageHistoryEntries here — doing so precisely inside a single SQL migration was
    /// judged not worth the added risk for what is dev/seed data at the time of writing. Companies
    /// with only a Vacancy and no Applications yet still get the six default stages seeded so
    /// RecruitmentStageSeeder's idempotency check (ticket #98) finds existing rows rather than
    /// re-seeding.
    /// </remarks>
    public partial class MigrateApplicationStatusToRecruitmentStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recruitment_stages",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_terminal = table.Column<bool>(type: "boolean", nullable: false),
                    terminal_outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recruitment_stages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recruitment_stages_company_id",
                schema: "recruitment",
                table: "recruitment_stages",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_recruitment_stages_company_id_display_order",
                schema: "recruitment",
                table: "recruitment_stages",
                columns: new[] { "company_id", "display_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recruitment_stages_company_id_name",
                schema: "recruitment",
                table: "recruitment_stages",
                columns: new[] { "company_id", "name" },
                unique: true);

            // Seed the six default stages (see RecruitmentStageSeeder for the canonical list this
            // mirrors) for every company that already has Vacancy and/or Application rows, so existing
            // companies are never left without a pipeline to migrate onto.
            migrationBuilder.Sql(@"
                INSERT INTO recruitment.recruitment_stages
                    (id, company_id, name, display_order, is_active, is_terminal, terminal_outcome, created_at, updated_at)
                SELECT
                    gen_random_uuid(), c.company_id, stage.name, stage.display_order, true, stage.is_terminal, stage.terminal_outcome, now(), now()
                FROM (
                    SELECT company_id FROM recruitment.vacancies
                    UNION
                    SELECT company_id FROM recruitment.applications
                ) c
                CROSS JOIN (VALUES
                    ('Application Received', 1, false, 'None'),
                    ('CV Review',            2, false, 'None'),
                    ('Interview',            3, false, 'None'),
                    ('Offer',                4, false, 'None'),
                    ('Hired',                5, true,  'Hired'),
                    ('Rejected',             6, true,  'Rejected')
                ) AS stage(name, display_order, is_terminal, terminal_outcome)
                WHERE NOT EXISTS (
                    SELECT 1 FROM recruitment.recruitment_stages rs WHERE rs.company_id = c.company_id
                );
            ");

            migrationBuilder.AddColumn<Guid>(
                name: "current_stage_id",
                schema: "recruitment",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "withdrawn_at",
                schema: "recruitment",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "new_stage_id",
                schema: "recruitment",
                table: "application_stage_history_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "previous_stage_id",
                schema: "recruitment",
                table: "application_stage_history_entries",
                type: "uuid",
                nullable: true);

            // Backfill applications.current_stage_id from the old status column, mapping onto the
            // matching seeded stage name for that company (see the class remarks for the full mapping
            // and the Withdrawn judgement call).
            migrationBuilder.Sql(@"
                UPDATE recruitment.applications a
                SET current_stage_id = rs.id,
                    withdrawn_at = CASE WHEN a.status = 'Withdrawn' THEN a.updated_at ELSE a.withdrawn_at END
                FROM recruitment.recruitment_stages rs
                WHERE rs.company_id = a.company_id
                  AND rs.name = CASE a.status
                        WHEN 'Applied'            THEN 'Application Received'
                        WHEN 'Screening'           THEN 'CV Review'
                        WHEN 'InterviewScheduled'  THEN 'Interview'
                        WHEN 'Interviewed'         THEN 'Interview'
                        WHEN 'Offered'             THEN 'Offer'
                        WHEN 'Hired'               THEN 'Hired'
                        WHEN 'Rejected'            THEN 'Rejected'
                        WHEN 'Withdrawn'           THEN 'Application Received'
                        ELSE 'Application Received'
                      END;
            ");

            migrationBuilder.Sql(@"
                UPDATE recruitment.application_stage_history_entries e
                SET new_stage_id = rs.id
                FROM recruitment.recruitment_stages rs
                WHERE rs.company_id = e.company_id
                  AND rs.name = CASE e.new_stage
                        WHEN 'Applied'            THEN 'Application Received'
                        WHEN 'Screening'           THEN 'CV Review'
                        WHEN 'InterviewScheduled'  THEN 'Interview'
                        WHEN 'Interviewed'         THEN 'Interview'
                        WHEN 'Offered'             THEN 'Offer'
                        WHEN 'Hired'               THEN 'Hired'
                        WHEN 'Rejected'            THEN 'Rejected'
                        WHEN 'Withdrawn'           THEN 'Application Received'
                        ELSE 'Application Received'
                      END;
            ");

            migrationBuilder.Sql(@"
                UPDATE recruitment.application_stage_history_entries e
                SET previous_stage_id = rs.id
                FROM recruitment.recruitment_stages rs
                WHERE rs.company_id = e.company_id
                  AND rs.name = CASE e.previous_stage
                        WHEN 'Applied'            THEN 'Application Received'
                        WHEN 'Screening'           THEN 'CV Review'
                        WHEN 'InterviewScheduled'  THEN 'Interview'
                        WHEN 'Interviewed'         THEN 'Interview'
                        WHEN 'Offered'             THEN 'Offer'
                        WHEN 'Hired'               THEN 'Hired'
                        WHEN 'Rejected'            THEN 'Rejected'
                        WHEN 'Withdrawn'           THEN 'Application Received'
                        ELSE 'Application Received'
                      END;
            ");

            migrationBuilder.AlterColumn<Guid>(
                name: "current_stage_id",
                schema: "recruitment",
                table: "applications",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "new_stage_id",
                schema: "recruitment",
                table: "application_stage_history_entries",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "status",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "new_stage",
                schema: "recruitment",
                table: "application_stage_history_entries");

            migrationBuilder.DropColumn(
                name: "previous_stage",
                schema: "recruitment",
                table: "application_stage_history_entries");

            migrationBuilder.CreateIndex(
                name: "IX_applications_current_stage_id",
                schema: "recruitment",
                table: "applications",
                column: "current_stage_id");

            migrationBuilder.AddForeignKey(
                name: "FK_applications_recruitment_stages_current_stage_id",
                schema: "recruitment",
                table: "applications",
                column: "current_stage_id",
                principalSchema: "recruitment",
                principalTable: "recruitment_stages",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_applications_recruitment_stages_current_stage_id",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropIndex(
                name: "IX_applications_current_stage_id",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "recruitment",
                table: "applications",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Applied");

            migrationBuilder.AddColumn<string>(
                name: "new_stage",
                schema: "recruitment",
                table: "application_stage_history_entries",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Applied");

            migrationBuilder.AddColumn<string>(
                name: "previous_stage",
                schema: "recruitment",
                table: "application_stage_history_entries",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Applied");

            migrationBuilder.DropColumn(
                name: "current_stage_id",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "withdrawn_at",
                schema: "recruitment",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "new_stage_id",
                schema: "recruitment",
                table: "application_stage_history_entries");

            migrationBuilder.DropColumn(
                name: "previous_stage_id",
                schema: "recruitment",
                table: "application_stage_history_entries");

            migrationBuilder.DropTable(
                name: "recruitment_stages",
                schema: "recruitment");
        }
    }
}
