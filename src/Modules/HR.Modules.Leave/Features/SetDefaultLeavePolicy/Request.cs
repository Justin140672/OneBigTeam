namespace HR.Modules.Leave.Features.SetDefaultLeavePolicy;

internal sealed record SetDefaultLeavePolicyRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
