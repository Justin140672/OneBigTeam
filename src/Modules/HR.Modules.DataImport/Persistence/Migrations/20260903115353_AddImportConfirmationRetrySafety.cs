using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.DataImport.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportConfirmationRetrySafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "confirmed_at",
                schema: "data_import",
                table: "import_staging_employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_employee_id",
                schema: "data_import",
                table: "import_staging_employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "data_import",
                table: "import_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "confirmed_at",
                schema: "data_import",
                table: "import_staging_employees");

            migrationBuilder.DropColumn(
                name: "created_employee_id",
                schema: "data_import",
                table: "import_staging_employees");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "data_import",
                table: "import_sessions");
        }
    }
}
