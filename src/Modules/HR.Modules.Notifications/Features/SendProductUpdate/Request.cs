namespace HR.Modules.Notifications.Features.SendProductUpdate;

internal sealed record SendProductUpdateRequest(
    string Title,
    string Message,
    string? Url);
