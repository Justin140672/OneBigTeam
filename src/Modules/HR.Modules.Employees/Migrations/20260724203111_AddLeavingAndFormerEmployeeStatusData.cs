using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddLeavingAndFormerEmployeeStatusData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The Terminated status is retired in favour of Leaving/FormerEmployee (see
            // EmploymentStatus.cs). Existing rows persisted the literal string "Terminated" in
            // this HasConversion<string>() column — migrate them to "FormerEmployee" so they keep
            // meaning "no longer employed" under the new status set.
            migrationBuilder.Sql(
                "UPDATE employees.employees SET status = 'FormerEmployee' WHERE status = 'Terminated';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE employees.employees SET status = 'Terminated' WHERE status = 'FormerEmployee';");
        }
    }
}
