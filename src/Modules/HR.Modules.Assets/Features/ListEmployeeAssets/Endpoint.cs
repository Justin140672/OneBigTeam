using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Assets.Features.ListEmployeeAssets;

internal sealed class Endpoint(ListEmployeeAssetsHandler handler)
    : Endpoint<ListEmployeeAssetsRequest, List<ListEmployeeAssetsResponse>>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/assets");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(ListEmployeeAssetsRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
