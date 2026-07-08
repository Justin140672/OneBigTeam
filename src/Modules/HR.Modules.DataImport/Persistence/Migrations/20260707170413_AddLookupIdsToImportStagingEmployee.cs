using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.DataImport.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLookupIdsToImportStagingEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "department_id",
                schema: "data_import",
                table: "import_staging_employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "employment_type_id",
                schema: "data_import",
                table: "import_staging_employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "location_id",
                schema: "data_import",
                table: "import_staging_employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "position_profile_id",
                schema: "data_import",
                table: "import_staging_employees",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "department_id",
                schema: "data_import",
                table: "import_staging_employees");

            migrationBuilder.DropColumn(
                name: "employment_type_id",
                schema: "data_import",
                table: "import_staging_employees");

            migrationBuilder.DropColumn(
                name: "location_id",
                schema: "data_import",
                table: "import_staging_employees");

            migrationBuilder.DropColumn(
                name: "position_profile_id",
                schema: "data_import",
                table: "import_staging_employees");
        }
    }
}
