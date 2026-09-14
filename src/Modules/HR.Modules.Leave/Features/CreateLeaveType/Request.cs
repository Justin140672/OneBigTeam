using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Features.CreateLeaveType;

internal sealed record CreateLeaveTypeRequest
{
    public Guid CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public int DefaultEntitlementDays { get; init; }
    public AccrualMethod AccrualMethod { get; init; }
    public LeaveTypeBehaviour Behaviour { get; init; }
    public bool HasBalance { get; init; } = true;
    public int? ToilExpiryDays { get; init; }
    public bool AllowNegativeToilBalance { get; init; }

    // Populated by the endpoint from the authenticated user's "sub" claim — never bound from the
    // client body (internal properties are not touched by FastEndpoints' JSON model binding).
    internal Guid? ActorEmployeeId { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
