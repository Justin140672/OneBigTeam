namespace HR.Modules.Notifications.Features.ResolveOperationalAlert;

internal sealed record ResolveOperationalAlertResponse(
    Guid Id,
    string Status,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedByUserId);
