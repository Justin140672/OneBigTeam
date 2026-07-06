using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <inheritdoc />
    public partial class AddInterviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "interviews",
                schema: "recruitment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interviewer_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interviews", x => x.id);
                    table.ForeignKey(
                        name: "FK_interviews_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "recruitment",
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_interviews_application_id_scheduled_at",
                schema: "recruitment",
                table: "interviews",
                columns: new[] { "application_id", "scheduled_at" });

            migrationBuilder.CreateIndex(
                name: "IX_interviews_company_id",
                schema: "recruitment",
                table: "interviews",
                column: "company_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "interviews",
                schema: "recruitment");
        }
    }
}
