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
}
