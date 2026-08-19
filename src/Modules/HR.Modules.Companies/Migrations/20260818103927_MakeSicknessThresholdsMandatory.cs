using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class MakeSicknessThresholdsMandatory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill existing NULL rows (companies that had these thresholds turned off) to the
            // new mandatory defaults before the columns are altered to NOT NULL below — otherwise
            // the ALTER COLUMN would fail against any row that still has a NULL value.
            migrationBuilder.Sql(
                "UPDATE companies.company_settings SET return_to_work_required_after_days = 1 " +
                "WHERE return_to_work_required_after_days IS NULL;");
            migrationBuilder.Sql(
                "UPDATE companies.company_settings SET fit_note_required_after_days = 7 " +
                "WHERE fit_note_required_after_days IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "return_to_work_required_after_days",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "fit_note_required_after_days",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: false,
                defaultValue: 7,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "return_to_work_required_after_days",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.AlterColumn<int>(
                name: "fit_note_required_after_days",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 7);
        }
    }
}
