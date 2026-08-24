using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddProbationCheckpointsToCompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "probation_checkpoint_day_1",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: true,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "probation_checkpoint_day_2",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: true,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "probation_checkpoint_day_3",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: true,
                defaultValue: 90);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "probation_checkpoint_day_1",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "probation_checkpoint_day_2",
                schema: "companies",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "probation_checkpoint_day_3",
                schema: "companies",
                table: "company_settings");
        }
    }
}
