using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Assets.Features.GetAsset;

internal sealed class Endpoint(GetAssetHandler handler, ICurrentUser currentUser)
    : Endpoint<GetAssetRequest, GetAssetResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/assets/{id:guid}");
        Policies("asset:view");
        // Note: Resource-level authorization (ownership checks) is performed in the handler using inline checks.
        // Endpoint-level policy only verifies the caller has the "asset:view" permission.
    }

    public override async Task HandleAsync(GetAssetRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, currentUser.UserId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: StatusCodes.Status404NotFound));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
