namespace HR.Modules.Leave.Features.ListLeavePolicies;

internal sealed record ListLeavePoliciesRequest
{
    public Guid CompanyId { get; init; }
    public bool? ActiveOnly { get; init; }
}
