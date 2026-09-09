namespace HR.Modules.Notifications.Features.ResolveOperationalAlert;

internal sealed record ResolveOperationalAlertRequest(Guid AlertId, string? ResolutionNote);
