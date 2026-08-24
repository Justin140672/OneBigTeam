using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Leave.Migrations
{
    /// <inheritdoc />
    // Hard-deletes the seeded "Sick Leave" LeaveType (and any dependent LeaveBalance,
    // LeaveBalanceAdjustment and LeaveRequest rows) for every company that already has it, in
    // addition to it already being excluded from LeaveTypeDefaultsProvisioner's seed set for new
    // companies. There are no live production companies yet, so a hard delete (rather than
    // soft-delete/preservation) is acceptable here — this migration is not reversible.
    public partial class RemoveSickLeaveType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM leave.leave_balance_adjustments
                WHERE leave_type_id IN (SELECT id FROM leave.leave_types WHERE name = 'Sick Leave');

                DELETE FROM leave.leave_requests
                WHERE leave_type_id IN (SELECT id FROM leave.leave_types WHERE name = 'Sick Leave');

                DELETE FROM leave.leave_balances
                WHERE leave_type_id IN (SELECT id FROM leave.leave_types WHERE name = 'Sick Leave');

                DELETE FROM leave.leave_types WHERE name = 'Sick Leave';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible data deletion — no Down migration.
        }
    }
}
