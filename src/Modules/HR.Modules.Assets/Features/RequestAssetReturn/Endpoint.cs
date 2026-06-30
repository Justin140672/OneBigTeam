using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Assets.Features.RequestAssetReturn;

internal sealed class Endpoint(RequestAssetReturnHandler handler)
    : Endpoint<RequestAssetReturnRequest>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/asset-assignments/{id:guid}/request-return");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(RequestAssetReturnRequest request, CancellationToken cancellationToken)
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
