namespace HR.Modules.Leave.Features.GetLeaveBalanceHistory;

internal sealed record GetLeaveBalanceHistoryResponse(
    Guid EmployeeId,
    Guid LeaveTypeId,
    IReadOnlyList<LeaveBalanceHistoryItem> Items);

/// <summary>
/// One row in the balance history grid for a given employee/leave type, sorted by
/// <see cref="Date"/> descending.
/// </summary>
/// <param name="Category">"ApprovedLeave" | "CancelledLeave" | "ToilAward" | "ManualAdjustment" | "CarryOver".</param>
/// <param name="LeaveTypeName">Included per-row (rather than only at the response root) so the
/// grid shape stays reusable if this endpoint is ever generalised to span multiple leave types.</param>
/// <param name="Change">Signed hours: negative when the event consumed balance (leave taken),
/// positive when it added to the balance (leave cancelled/reversed, TOIL award, a positive manual
/// adjustment or carry-over). Manual adjustments/carry-over use their actual signed
/// <c>AdjustmentHours</c> value as-is.</param>
/// <param name="Reason">The <c>LeaveBalanceAdjustmentReason</c> name for manual adjustments/carry-over;
/// a fixed descriptive label ("Leave Taken", "Leave Cancelled", "TOIL Award") for the other categories,
/// which have no reason enum of their own.</param>
/// <param name="BalanceAfter">Running balance in hours immediately after this event. Computed by
/// anchoring to the current policy year's known remaining balance and working backwards/forwards
/// through the full chronological event list — see the handler for the exact method and its
/// limitations (there is no true "starting balance" record, so this is only as accurate as the
/// full set of events considered).</param>
/// <param name="CreatedBy">Display name of the employee who caused this change: the reviewing
/// manager for approved leave, the employee themselves for a self-service cancellation (no other
/// actor is tracked for cancellation), the awarding employee for TOIL, or the adjusting employee
/// for manual adjustments/carry-over.</param>
internal sealed record LeaveBalanceHistoryItem(
    string Category,
    DateTimeOffset Date,
    string LeaveTypeName,
    decimal Change,
    string Reason,
    decimal BalanceAfter,
    string CreatedBy,
    string Description);
