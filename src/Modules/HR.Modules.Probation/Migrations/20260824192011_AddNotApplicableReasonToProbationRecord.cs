using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Probation.Migrations
{
    /// <inheritdoc />
    public partial class AddNotApplicableReasonToProbationRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "not_applicable_reason",
                schema: "probation",
                table: "probation_records",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "not_applicable_reason",
                schema: "probation",
                table: "probation_records");
        }
    }
}
