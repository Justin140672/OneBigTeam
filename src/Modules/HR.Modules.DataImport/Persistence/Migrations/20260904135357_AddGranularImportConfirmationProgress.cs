using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.DataImport.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGranularImportConfirmationProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "employee_created_at",
                schema: "data_import",
                table: "import_staging_employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "employee_created_event_published_at",
                schema: "data_import",
                table: "import_staging_employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "employee_imported_event_published_at",
                schema: "data_import",
                table: "import_staging_employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "manager_assignment_processed_at",
                schema: "data_import",
                table: "import_staging_employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "opening_leave_balance_processed_at",
                schema: "data_import",
                table: "import_staging_employees",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "employee_created_at",
                schema: "data_import",
                table: "import_staging_employees");

            migrationBuilder.DropColumn(
                name: "employee_created_event_published_at",
                schema: "data_import",
                table: "import_staging_employees");

            migrationBuilder.DropColumn(
                name: "employee_imported_event_published_at",
                schema: "data_import",
                table: "import_staging_employees");

            migrationBuilder.DropColumn(
                name: "manager_assignment_processed_at",
                schema: "data_import",
                table: "import_staging_employees");

            migrationBuilder.DropColumn(
                name: "opening_leave_balance_processed_at",
                schema: "data_import",
                table: "import_staging_employees");
        }
    }
}
