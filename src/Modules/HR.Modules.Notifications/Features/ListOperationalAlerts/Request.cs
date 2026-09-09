namespace HR.Modules.Notifications.Features.ListOperationalAlerts;

internal sealed record ListOperationalAlertsRequest(
    Guid? CompanyId,
    string? Category,
    string? Status,
    int Page = 1,
    int PageSize = 25);
