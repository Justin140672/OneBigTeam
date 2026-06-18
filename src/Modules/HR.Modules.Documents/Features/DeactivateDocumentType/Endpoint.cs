using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.DeactivateDocumentType;

internal sealed class Endpoint(DeactivateDocumentTypeHandler handler)
    : Endpoint<DeactivateDocumentTypeRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/document-types/{documentTypeId:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        DeactivateDocumentTypeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await SendResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
                return;
            }

            await SendResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await SendNoContentAsync(cancellationToken);
    }
}
