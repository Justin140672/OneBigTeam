using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Features.AdjustLeaveBalance;

internal sealed record AdjustLeaveBalanceRequest(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    decimal AdjustmentValue,
    LeaveBalanceAdjustmentReason Reason,
    string? Comments,
    bool AllowNegativeOverride)
{
    // Populated by the endpoint from the authenticated user's "sub" claim — never bound from the
    // client body (internal properties are not touched by FastEndpoints' JSON model binding).
    internal Guid AdjustedByEmployeeId { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
