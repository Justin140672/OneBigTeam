using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ListRequiredAssetsForPositionProfile;

internal sealed class Endpoint(ListRequiredAssetsHandler handler)
    : Endpoint<ListRequiredAssetsRequest, ListRequiredAssetsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/position-profiles/{positionProfileId:guid}/required-assets");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        ListRequiredAssetsRequest request,
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
