using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Assets.Features.RetireAsset;

internal sealed class Endpoint(RetireAssetHandler handler)
    : Endpoint<RetireAssetRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/assets/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(RetireAssetRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure && result.Error.Code == "conflict")
        {
            await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
            return;
        }

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.NoContent());
    }
}
