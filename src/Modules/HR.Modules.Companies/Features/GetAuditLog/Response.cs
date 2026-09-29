namespace HR.Modules.Companies.Features.GetAuditLog;

internal sealed record GetAuditLogResponse(
    IReadOnlyList<AuditLogItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages,
    IReadOnlyList<string> AvailableEventTypes);

internal sealed record AuditLogItem(
    DateTimeOffset OccurredAt,
    string EventType,
    string EntityType,
    Guid? CompanyId,
    string? CompanyName,
    Guid? ActorUserId,
    string? AdministratorEmail,
    string? Summary);
