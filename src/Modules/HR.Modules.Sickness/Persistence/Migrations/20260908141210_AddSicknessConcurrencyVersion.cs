using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Sickness.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSicknessConcurrencyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "sickness",
                table: "sickness_records",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "sickness",
                table: "sickness_categories",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                schema: "sickness",
                table: "sickness_records");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "sickness",
                table: "sickness_categories");
        }
    }
}
