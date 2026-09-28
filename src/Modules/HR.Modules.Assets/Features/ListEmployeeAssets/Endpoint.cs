using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Assets.Features.ListEmployeeAssets;

internal sealed class Endpoint(ListEmployeeAssetsHandler handler, ICurrentUser currentUser)
    : Endpoint<ListEmployeeAssetsRequest, List<ListEmployeeAssetsResponse>>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/assets");
        Policies("asset:view");
        // Note: Resource-level authorization (ownership checks) is performed in the handler using inline checks.
        // Endpoint-level policy only verifies the caller has the "asset:view" permission.
    }

    public override async Task HandleAsync(ListEmployeeAssetsRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, currentUser.UserId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
