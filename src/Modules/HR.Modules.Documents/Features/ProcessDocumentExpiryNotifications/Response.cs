namespace HR.Modules.Documents.Features.ProcessDocumentExpiryNotifications;

internal sealed record ProcessDocumentExpiryNotificationsResponse(
    int ExpiringSoonCount,
    int ExpiredCount,
    int Reminder90Count = 0,
    int Reminder30Count = 0,
    int Reminder7Count = 0);
