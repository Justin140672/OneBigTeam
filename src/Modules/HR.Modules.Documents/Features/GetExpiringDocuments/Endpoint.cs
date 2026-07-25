using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.GetExpiringDocuments;

internal sealed class Endpoint(GetExpiringDocumentsHandler handler)
    : Endpoint<GetExpiringDocumentsRequest, GetExpiringDocumentsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/documents/expiring");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetExpiringDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
