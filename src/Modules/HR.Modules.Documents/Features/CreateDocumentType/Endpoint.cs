using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.CreateDocumentType;

internal sealed class Endpoint(CreateDocumentTypeHandler handler)
    : Endpoint<CreateDocumentTypeRequest, CreateDocumentTypeResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/document-types");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        CreateDocumentTypeRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/document-types/{result.Value.Id}",
            result.Value));
    }
}
