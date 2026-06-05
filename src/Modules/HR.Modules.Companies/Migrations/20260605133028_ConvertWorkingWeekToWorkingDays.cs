using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class ConvertWorkingWeekToWorkingDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE companies.company_settings
                ALTER COLUMN working_week TYPE integer
                USING (
                    CASE working_week
                        WHEN 'Monday-Friday' THEN 31
                        WHEN 'Sunday-Thursday' THEN 79
                        ELSE 0
                    END
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE companies.company_settings
                ALTER COLUMN working_week TYPE character varying(30)
                USING (
                    CASE working_week
                        WHEN 31 THEN 'Monday-Friday'
                        WHEN 79 THEN 'Sunday-Thursday'
                        ELSE 'Monday-Friday'
                    END
                );
                """);
        }
    }
}
