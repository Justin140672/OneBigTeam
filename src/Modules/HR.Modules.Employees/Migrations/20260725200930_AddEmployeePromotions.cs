using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeePromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_promotions",
                schema: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_position_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_position_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_manager_id = table.Column<Guid>(type: "uuid", nullable: true),
                    new_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    compensation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_promotions", x => x.id);
                    table.ForeignKey(
                        name: "FK_employee_promotions_compensations_compensation_id",
                        column: x => x.compensation_id,
                        principalSchema: "employees",
                        principalTable: "compensations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_promotions_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "employees",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_promotions_employees_new_manager_id",
                        column: x => x.new_manager_id,
                        principalSchema: "employees",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_promotions_locations_new_location_id",
                        column: x => x.new_location_id,
                        principalSchema: "employees",
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_promotions_position_profiles_new_position_profile_~",
                        column: x => x.new_position_profile_id,
                        principalSchema: "employees",
                        principalTable: "position_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_promotions_position_profiles_previous_position_pro~",
                        column: x => x.previous_position_profile_id,
                        principalSchema: "employees",
                        principalTable: "position_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_promotions_company_id",
                schema: "employees",
                table: "employee_promotions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_employee_promotions_company_id_employee_id",
                schema: "employees",
                table: "employee_promotions",
                columns: new[] { "company_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "IX_employee_promotions_compensation_id",
                schema: "employees",
                table: "employee_promotions",
                column: "compensation_id");

            migrationBuilder.CreateIndex(
                name: "IX_employee_promotions_employee_id",
                schema: "employees",
                table: "employee_promotions",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "IX_employee_promotions_new_location_id",
                schema: "employees",
                table: "employee_promotions",
                column: "new_location_id");

            migrationBuilder.CreateIndex(
                name: "IX_employee_promotions_new_manager_id",
                schema: "employees",
                table: "employee_promotions",
                column: "new_manager_id");

            migrationBuilder.CreateIndex(
                name: "IX_employee_promotions_new_position_profile_id",
                schema: "employees",
                table: "employee_promotions",
                column: "new_position_profile_id");

            migrationBuilder.CreateIndex(
                name: "IX_employee_promotions_previous_position_profile_id",
                schema: "employees",
                table: "employee_promotions",
                column: "previous_position_profile_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_promotions",
                schema: "employees");
        }
    }
}
