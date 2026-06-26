using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Probation.Migrations
{
    /// <inheritdoc />
    public partial class AddOutcomeToProbationReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "outcome",
                schema: "probation",
                table: "probation_reviews",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "outcome",
                schema: "probation",
                table: "probation_reviews");
        }
    }
}
