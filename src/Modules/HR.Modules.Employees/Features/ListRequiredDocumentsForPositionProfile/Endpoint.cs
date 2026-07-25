using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ListRequiredDocumentsForPositionProfile;

internal sealed class Endpoint(ListRequiredDocumentsHandler handler)
    : Endpoint<ListRequiredDocumentsRequest, ListRequiredDocumentsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/position-profiles/{positionProfileId:guid}/required-documents");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        ListRequiredDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
