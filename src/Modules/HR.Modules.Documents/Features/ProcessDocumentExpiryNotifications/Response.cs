namespace HR.Modules.Documents.Features.ProcessDocumentExpiryNotifications;

/// <summary>
/// DOC-03: counts for each of the three independent upcoming-expiry reminder stages plus the
/// existing overdue/expired stage. ExpiringSoonCount is retained for backward compatibility and
/// is always the sum of the three staged counts.
/// </summary>
internal sealed record ProcessDocumentExpiryNotificationsResponse(
    int ExpiringSoonCount,
    int ExpiredCount,
    int Reminder90Count = 0,
    int Reminder30Count = 0,
    int Reminder7Count = 0);
