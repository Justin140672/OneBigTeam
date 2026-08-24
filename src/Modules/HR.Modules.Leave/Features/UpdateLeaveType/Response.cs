namespace HR.Modules.Leave.Features.UpdateLeaveType;

internal sealed record UpdateLeaveTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string Code,
    int DefaultEntitlementDays,
    string AccrualMethod,
    string Behaviour,
    bool IsActive,
    bool HasBalance,
    bool IsSystem,
    int? ToilExpiryDays,
    bool AllowNegativeToilBalance,
    DateTimeOffset UpdatedAt);
