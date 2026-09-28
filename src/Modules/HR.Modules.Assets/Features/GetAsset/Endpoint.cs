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
        // Endpoint-level policy only verifies the caller has the "asset:view" permission; the handler further
        // restricts access to assigned employees (and HR admins/managers via future integration of AssetResourceAuthorizer).
    }

    public override async Task HandleAsync(GetAssetRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, currentUser.UserId, cancellationToken);

        if (result.IsFailure)
        {
            var statusCode = result.Error.Code == "forbidden"
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status404NotFound;
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: statusCode));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
