using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.GetExpiringDocuments;

internal sealed class Endpoint(GetExpiringDocumentsHandler handler)
    : Endpoint<GetExpiringDocumentsRequest, GetExpiringDocumentsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/documents/expiring");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        GetExpiringDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await SendAsync(result.Value!, StatusCodes.Status200OK, cancellationToken);
    }
}
