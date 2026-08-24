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
    bool HasBalance,
    bool IsSystem,
    int? ToilExpiryDays,
    bool AllowNegativeToilBalance,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
