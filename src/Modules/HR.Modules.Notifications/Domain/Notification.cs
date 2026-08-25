using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Notifications.Domain;

internal sealed class Notification
{
    private Notification() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Body { get; private set; }
    public bool IsRead { get; private set; }
    public Guid SourceEntityId { get; private set; }
    public NotificationType Type { get; private set; }
    public NotificationPriority Priority { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// NOT-04: application-relative navigation target computed once at write time by
    /// NotificationActionRouteBuilder (never recomputed per read). Null for notification types
    /// with no natural destination (purely informational). Always either null or a same-origin
    /// relative path — see NotificationActionRouteBuilder.EnforceRelative for the invariant this
    /// column depends on; external/absolute URLs must never reach this column.
    /// </summary>
    public string? ActionUrl { get; private set; }

    public static Notification Create(
        Guid id, Guid companyId, Guid employeeId,
        string title, string? body,
        Guid sourceEntityId, DateTimeOffset now,
        NotificationType type = NotificationType.TaskAssigned,
        NotificationPriority priority = NotificationPriority.Normal,
        string? actionUrl = null) => new()
    {
        Id             = id,
        CompanyId      = companyId,
        EmployeeId     = employeeId,
        Title          = title,
        Body           = body,
        IsRead         = false,
        SourceEntityId = sourceEntityId,
        Type           = type,
        Priority       = priority,
        CreatedAt      = now,
        ActionUrl      = actionUrl,
    };

    public void MarkAsRead() => IsRead = true;
}
