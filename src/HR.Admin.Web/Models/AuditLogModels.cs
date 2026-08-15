namespace HR.Admin.Web.Models;

// Mirrors HR.Modules.Companies.Features.GetAuditLog.Response's shape exactly — same "app-local DTO
// matching the API contract" convention as every other *Models.cs file in this project.
public sealed record AuditLogResponse(
    IReadOnlyList<AuditLogItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages,
    IReadOnlyList<string> AvailableEventTypes);

public sealed record AuditLogItem(
    DateTimeOffset OccurredAt,
    string EventType,
    string EntityType,
    Guid? CompanyId,
    string? CompanyName,
    Guid? ActorUserId,
    string? AdministratorEmail,
    string? Summary);
