using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Sickness.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnToWorkReviewOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "adjustment_details",
                schema: "sickness",
                table: "return_to_work_reviews",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "adjustments_required",
                schema: "sickness",
                table: "return_to_work_reviews",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "fit_to_return_outcome",
                schema: "sickness",
                table: "return_to_work_reviews",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "adjustment_details",
                schema: "sickness",
                table: "return_to_work_reviews");

            migrationBuilder.DropColumn(
                name: "adjustments_required",
                schema: "sickness",
                table: "return_to_work_reviews");

            migrationBuilder.DropColumn(
                name: "fit_to_return_outcome",
                schema: "sickness",
                table: "return_to_work_reviews");
        }
    }
}
