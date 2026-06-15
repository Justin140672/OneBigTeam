namespace HR.Modules.Tasks.Features.MarkAllNotificationsRead;

internal sealed class MarkAllNotificationsReadRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
}
