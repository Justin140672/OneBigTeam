namespace HR.Modules.Leave.Features.DeactivateLeaveType;

internal sealed record DeactivateLeaveTypeRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
