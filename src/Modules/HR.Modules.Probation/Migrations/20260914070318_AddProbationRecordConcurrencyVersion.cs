using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Probation.Migrations
{
    /// <inheritdoc />
    public partial class AddProbationRecordConcurrencyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "probation",
                table: "probation_records",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                schema: "probation",
                table: "probation_records");
        }
    }
}
