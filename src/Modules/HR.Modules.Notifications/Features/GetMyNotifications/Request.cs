using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Features.GetMyNotifications;

internal sealed class GetMyNotificationsRequest
{
    public Guid CompanyId { get; init; }

    internal Guid EmployeeId { get; init; }

    public bool? IsRead { get; init; }

    public NotificationType? Type { get; init; }

    public NotificationPriority? Priority { get; init; }

    public DateTimeOffset? CreatedFrom { get; init; }

    public DateTimeOffset? CreatedTo { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}
