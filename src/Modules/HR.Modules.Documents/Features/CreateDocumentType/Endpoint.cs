using FastEndpoints;
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
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "conflict")
            {
                await SendResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await SendResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        HttpContext.Response.Headers.Location =
            $"/api/companies/{result.Value!.CompanyId}/document-types/{result.Value.Id}";

        await SendAsync(result.Value, StatusCodes.Status201Created, cancellationToken);
    }
}
