namespace HR.Admin.Web.Models;

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
    string? Summary)
{
    public string EventTypeText => HR.SharedKernel.EnumText.Humanize(EventType);
}
