namespace HR.Modules.Notifications.Features.GetUnreadNotificationCount;

internal sealed class GetUnreadNotificationCountRequest
{
    public Guid CompanyId { get; init; }

    internal Guid EmployeeId { get; init; }
}
