using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.ProcessDocumentExpiryNotifications;

internal sealed class Endpoint(ProcessDocumentExpiryNotificationsHandler handler)
    : Endpoint<ProcessDocumentExpiryNotificationsRequest, ProcessDocumentExpiryNotificationsResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/documents/expiry-notifications");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        ProcessDocumentExpiryNotificationsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await SendAsync(result, StatusCodes.Status200OK, cancellationToken);
    }
}
