namespace HR.Modules.Leave.Features.CreateLeaveType;

internal sealed record CreateLeaveTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string Code,
    int DefaultEntitlementDays,
    string AccrualMethod,
    string Behaviour,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
