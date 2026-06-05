using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_settings",
                schema: "companies",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    locale = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    working_week = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    leave_year_start_month = table.Column<int>(type: "integer", nullable: false),
                    default_holiday_allowance = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    probation_months = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_settings", x => x.company_id);
                    table.CheckConstraint("CK_company_settings_leave_year_start_month", "leave_year_start_month BETWEEN 1 AND 12");
                    table.ForeignKey(
                        name: "FK_company_settings_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "companies",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO companies.company_settings (
                    company_id,
                    time_zone,
                    locale,
                    working_week,
                    leave_year_start_month,
                    default_holiday_allowance,
                    probation_months,
                    created_at,
                    updated_at)
                SELECT
                    c.id,
                    'UTC',
                    'en-GB',
                    'Monday-Friday',
                    1,
                    25.00,
                    6,
                    NOW(),
                    NOW()
                FROM companies.companies AS c
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM companies.company_settings AS settings
                    WHERE settings.company_id = c.id
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_settings",
                schema: "companies");
        }
    }
}
