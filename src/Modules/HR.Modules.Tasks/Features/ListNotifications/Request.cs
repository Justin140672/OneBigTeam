namespace HR.Modules.Tasks.Features.ListNotifications;

internal sealed class ListNotificationsRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
}
