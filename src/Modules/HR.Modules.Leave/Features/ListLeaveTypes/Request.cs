namespace HR.Modules.Leave.Features.ListLeaveTypes;

internal sealed record ListLeaveTypesRequest
{
    public Guid CompanyId { get; init; }
    public bool? IsActive { get; init; }
}
