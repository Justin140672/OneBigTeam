using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Leave.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveBalanceAdjustmentDaysColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename the existing column so pre-existing rows keep their values (a plain
            // rename, not drop+add). Historical rows were all entered as hours under the old
            // system, so their numeric values will read verbatim as "days" after this rename —
            // an accepted, documented tradeoff for existing historical rows in this dev/demo
            // environment.
            migrationBuilder.RenameColumn(
                name: "adjustment_hours",
                schema: "leave",
                table: "leave_balance_adjustments",
                newName: "adjustment_days");

            // New nullable column populated only for TOIL adjustments going forward; existing
            // rows get NULL.
            migrationBuilder.AddColumn<decimal>(
                name: "adjustment_hours",
                schema: "leave",
                table: "leave_balance_adjustments",
                type: "numeric(6,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "adjustment_hours",
                schema: "leave",
                table: "leave_balance_adjustments");

            migrationBuilder.RenameColumn(
                name: "adjustment_days",
                schema: "leave",
                table: "leave_balance_adjustments",
                newName: "adjustment_hours");
        }
    }
}
