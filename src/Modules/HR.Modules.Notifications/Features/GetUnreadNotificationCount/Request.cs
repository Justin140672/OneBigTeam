namespace HR.Modules.Notifications.Features.GetUnreadNotificationCount;

internal sealed class GetUnreadNotificationCountRequest
{
    public Guid CompanyId { get; init; }

    // Populated by the endpoint from the authenticated user's sub claim.
    internal Guid EmployeeId { get; init; }
}
