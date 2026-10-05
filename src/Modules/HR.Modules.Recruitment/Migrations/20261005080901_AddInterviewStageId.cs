using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddInterviewStageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "stage_id",
                schema: "recruitment",
                table: "interviews",
                type: "uuid",
                nullable: true);

            // Backfill, most to least reliable:
            // 1. The stage the application occupied when the interview was created, taken from stage history
            //    (latest transition at or before interview created_at; before any transition, the earliest
            //    transition's previous stage). Only accepted when that stage is a non-terminal Interview stage.
            // 1b. A still-pending interview belongs to the application's current stage when that is an Interview stage.
            // 2. When the company has exactly one non-terminal Interview stage there is no ambiguity.
            // 3. Otherwise stage_id stays NULL: a legacy interview never counts as passed for any stage,
            //    so ambiguous history cannot unlock an offer or a later interview stage.
            migrationBuilder.Sql(
                """
                UPDATE recruitment.interviews i
                SET stage_id = s.id
                FROM recruitment.recruitment_stages s
                WHERE s.company_id = i.company_id
                  AND s.purpose = 'Interview'
                  AND s.is_terminal = false
                  AND s.id = COALESCE(
                      (SELECT h.new_stage_id
                         FROM recruitment.application_stage_history_entries h
                        WHERE h.application_id = i.application_id AND h.changed_at <= i.created_at
                        ORDER BY h.changed_at DESC, h.id DESC
                        LIMIT 1),
                      (SELECT h.previous_stage_id
                         FROM recruitment.application_stage_history_entries h
                        WHERE h.application_id = i.application_id
                        ORDER BY h.changed_at ASC, h.id ASC
                        LIMIT 1));
                """);

            migrationBuilder.Sql(
                """
                UPDATE recruitment.interviews i
                SET stage_id = s.id
                FROM recruitment.applications a
                JOIN recruitment.recruitment_stages s ON s.id = a.current_stage_id
                WHERE i.stage_id IS NULL
                  AND i.outcome = 'Pending'
                  AND a.id = i.application_id
                  AND s.purpose = 'Interview'
                  AND s.is_terminal = false;
                """);

            migrationBuilder.Sql(
                """
                UPDATE recruitment.interviews i
                SET stage_id = only_stage.id
                FROM (
                    SELECT company_id, (array_agg(id))[1] AS id
                    FROM recruitment.recruitment_stages
                    WHERE purpose = 'Interview' AND is_terminal = false
                    GROUP BY company_id
                    HAVING count(*) = 1
                ) only_stage
                WHERE i.stage_id IS NULL AND only_stage.company_id = i.company_id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_interviews_application_id_stage_id",
                schema: "recruitment",
                table: "interviews",
                columns: new[] { "application_id", "stage_id" });

            migrationBuilder.CreateIndex(
                name: "IX_interviews_stage_id",
                schema: "recruitment",
                table: "interviews",
                column: "stage_id");

            migrationBuilder.AddForeignKey(
                name: "FK_interviews_recruitment_stages_stage_id",
                schema: "recruitment",
                table: "interviews",
                column: "stage_id",
                principalSchema: "recruitment",
                principalTable: "recruitment_stages",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_interviews_recruitment_stages_stage_id",
                schema: "recruitment",
                table: "interviews");

            migrationBuilder.DropIndex(
                name: "IX_interviews_application_id_stage_id",
                schema: "recruitment",
                table: "interviews");

            migrationBuilder.DropIndex(
                name: "IX_interviews_stage_id",
                schema: "recruitment",
                table: "interviews");

            migrationBuilder.DropColumn(
                name: "stage_id",
                schema: "recruitment",
                table: "interviews");
        }
    }
}
