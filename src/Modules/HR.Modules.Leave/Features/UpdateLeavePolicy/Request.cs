namespace HR.Modules.Leave.Features.UpdateLeavePolicy;

internal sealed record UpdateLeavePolicyRequest
{
    public Guid CompanyId { get; init; }
    public Guid PolicyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int CarryOverDays { get; init; }
    public bool AllowNegativeBalance { get; init; }
    public bool IsDefault { get; init; }
    public bool RequiresApproval { get; init; } = true;

    // Ticket 2 (optimistic concurrency) — see UpdateEmployeeProfileRequest.ExpectedVersion.
    public int? ExpectedVersion { get; init; }

    internal Guid? ActorEmployeeId { get; init; }
}
