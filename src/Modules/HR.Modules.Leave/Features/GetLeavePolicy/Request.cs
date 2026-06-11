namespace HR.Modules.Leave.Features.GetLeavePolicy;

internal sealed record GetLeavePolicyRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
