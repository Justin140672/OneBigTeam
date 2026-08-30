namespace HR.Modules.Notifications.Features.AcknowledgeAdministrativeAlert;

internal sealed class AcknowledgeAdministrativeAlertRequest
{
    public Guid CompanyId { get; init; }
    public Guid AlertId { get; init; }

    /// <summary>Resolved server-side from ICurrentUser in the Endpoint — never bound from the route or body.</summary>
    public Guid ActorUserId { get; init; }
}
