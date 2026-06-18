namespace HR.Modules.Documents.Features.ProcessDocumentExpiryNotifications;

internal sealed record ProcessDocumentExpiryNotificationsRequest
{
    public Guid CompanyId { get; init; }
}
