using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.ListDocumentTypes;

internal sealed class Endpoint(ListDocumentTypesHandler handler)
    : Endpoint<ListDocumentTypesRequest, ListDocumentTypesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/document-types");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        ListDocumentTypesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await SendAsync(result.Value!, StatusCodes.Status200OK, cancellationToken);
    }
}
