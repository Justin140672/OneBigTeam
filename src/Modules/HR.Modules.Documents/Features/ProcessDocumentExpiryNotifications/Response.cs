namespace HR.Modules.Documents.Features.ProcessDocumentExpiryNotifications;

internal sealed record ProcessDocumentExpiryNotificationsResponse(
    int ExpiringSoonCount,
    int ExpiredCount);
