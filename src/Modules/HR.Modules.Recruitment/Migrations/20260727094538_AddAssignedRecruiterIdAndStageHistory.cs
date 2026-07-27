using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignedRecruiterIdAndStageHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assigned_recruiter_id",
                schema: "recruitment",
                table: "vacancies",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "application_stage_history_entries",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    new_stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_stage_history_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_application_stage_history_entries_applications_application_~",
                        column: x => x.application_id,
                        principalSchema: "recruitment",
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_application_stage_history_entries_application_id",
                schema: "recruitment",
                table: "application_stage_history_entries",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "IX_application_stage_history_entries_company_id",
                schema: "recruitment",
                table: "application_stage_history_entries",
                column: "company_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_stage_history_entries",
                schema: "recruitment");

            migrationBuilder.DropColumn(
                name: "assigned_recruiter_id",
                schema: "recruitment",
                table: "vacancies");
        }
    }
}
