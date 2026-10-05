using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Reporting.Migrations
{
    /// <inheritdoc />
    public partial class MigrateDraftEmployeeStatusInSavedReportViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE reporting.saved_report_views
                SET filter_criteria_json = jsonb_set(filter_criteria_json, '{EmployeeStatus}', '"Active"'::jsonb)
                WHERE jsonb_typeof(filter_criteria_json) = 'object'
                  AND lower(filter_criteria_json ->> 'EmployeeStatus') = 'draft';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
