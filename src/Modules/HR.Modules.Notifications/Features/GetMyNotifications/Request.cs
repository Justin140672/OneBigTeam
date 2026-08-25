using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Features.GetMyNotifications;

// NOT-06: proper server-side history paging replacing the old hardcoded Take(50). Pagination shape
// mirrors DOC-06's SearchEmployeeDocumentsRequest (PageNumber/PageSize) for consistency within this
// session's work. Filters support read/unread, notification type, priority and a created-at date
// range, all combined with pagination in the same query.
internal sealed class GetMyNotificationsRequest
{
    public Guid CompanyId { get; init; }

    // Populated by the endpoint from the authenticated user's sub claim.
    internal Guid EmployeeId { get; init; }

    public bool? IsRead { get; init; }

    public NotificationType? Type { get; init; }

    public NotificationPriority? Priority { get; init; }

    public DateTimeOffset? CreatedFrom { get; init; }

    public DateTimeOffset? CreatedTo { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}
