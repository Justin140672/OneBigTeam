namespace HR.Modules.Notifications.Features.GetMyNotifications;

internal sealed class GetMyNotificationsRequest
{
    public Guid CompanyId { get; init; }

    // Populated by the endpoint from the authenticated user's sub claim.
    internal Guid EmployeeId { get; init; }
}
