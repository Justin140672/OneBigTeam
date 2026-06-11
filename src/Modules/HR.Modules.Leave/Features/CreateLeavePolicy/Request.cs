namespace HR.Modules.Leave.Features.CreateLeavePolicy;

internal sealed record CreateLeavePolicyRequest
{
    public Guid CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int CarryOverDays { get; init; }
    public bool AllowNegativeBalance { get; init; }
}
