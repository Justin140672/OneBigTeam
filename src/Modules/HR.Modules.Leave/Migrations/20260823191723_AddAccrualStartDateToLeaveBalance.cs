using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Leave.Migrations
{
    /// <inheritdoc />
    public partial class AddAccrualStartDateToLeaveBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default of DateOnly.MinValue backfills existing rows to a date far in the past, so
            // LeaveAccrualCalculator always treats a pre-existing balance as fully accrued
            // (asOfDate is never earlier than accrual_start_date) - preserving prior behaviour for
            // data created before LEAVE-04. All future inserts always set an explicit, real value.
            migrationBuilder.AddColumn<DateOnly>(
                name: "accrual_start_date",
                schema: "leave",
                table: "leave_balances",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "accrual_start_date",
                schema: "leave",
                table: "leave_balances");
        }
    }
}
