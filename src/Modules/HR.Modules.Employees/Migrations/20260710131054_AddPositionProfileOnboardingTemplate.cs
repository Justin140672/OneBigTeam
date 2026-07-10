using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionProfileOnboardingTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "position_profile_onboarding_templates",
                schema: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    onboarding_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_position_profile_onboarding_templates", x => x.id);
                    table.ForeignKey(
                        name: "FK_position_profile_onboarding_templates_position_profiles_pos~",
                        column: x => x.position_profile_id,
                        principalSchema: "employees",
                        principalTable: "position_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_position_profile_onboarding_templates_company_id",
                schema: "employees",
                table: "position_profile_onboarding_templates",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_position_profile_onboarding_templates_company_id_position_p~",
                schema: "employees",
                table: "position_profile_onboarding_templates",
                columns: new[] { "company_id", "position_profile_id", "onboarding_template_id" },
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_position_profile_onboarding_templates_onboarding_template_id",
                schema: "employees",
                table: "position_profile_onboarding_templates",
                column: "onboarding_template_id");

            migrationBuilder.CreateIndex(
                name: "IX_position_profile_onboarding_templates_position_profile_id",
                schema: "employees",
                table: "position_profile_onboarding_templates",
                column: "position_profile_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "position_profile_onboarding_templates",
                schema: "employees");
        }
    }
}
