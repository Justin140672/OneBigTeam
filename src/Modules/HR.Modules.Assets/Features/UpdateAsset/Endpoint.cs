using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Assets.Features.UpdateAsset;

internal sealed class Endpoint(UpdateAssetHandler handler)
    : Endpoint<UpdateAssetRequest, UpdateAssetResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/assets/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(UpdateAssetRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
