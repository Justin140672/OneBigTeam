namespace HR.Modules.Notifications.Features.MarkAdministrativeAlertRead;

internal sealed class MarkAdministrativeAlertReadRequest
{
    public Guid CompanyId { get; init; }
    public Guid AlertId { get; init; }
}
