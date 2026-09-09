using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UpdateDocumentType;

internal sealed class Endpoint(UpdateDocumentTypeHandler handler)
    : Endpoint<UpdateDocumentTypeRequest, UpdateDocumentTypeResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/document-types/{documentTypeId:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        UpdateDocumentTypeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
