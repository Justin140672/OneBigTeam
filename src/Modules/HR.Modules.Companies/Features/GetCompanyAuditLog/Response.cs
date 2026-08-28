namespace HR.Modules.Companies.Features.GetCompanyAuditLog;

internal sealed record GetCompanyAuditLogResponse(
    IReadOnlyList<CompanyAuditLogItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

internal sealed record CompanyAuditLogItem(
    DateTimeOffset OccurredAt,
    string EventType,
    string EntityType,
    Guid EntityId,
    Guid? EmployeeId,
    Guid? ActorUserId,
    /// <summary>Resolved display name for the actor — null if the actor is a system process.</summary>
    string? ActorDisplayName,
    string? Summary);
