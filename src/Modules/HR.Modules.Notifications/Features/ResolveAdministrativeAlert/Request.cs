namespace HR.Modules.Notifications.Features.ResolveAdministrativeAlert;

internal sealed class ResolveAdministrativeAlertRequest
{
    public Guid CompanyId { get; init; }
    public Guid AlertId { get; init; }

    public string? ResolutionNote { get; init; }

    /// <summary>Resolved server-side from ICurrentUser in the Endpoint — never bound from the route or body.</summary>
    public Guid ActorUserId { get; init; }
}
