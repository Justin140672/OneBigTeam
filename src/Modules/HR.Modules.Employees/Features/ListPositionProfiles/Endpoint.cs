using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ListPositionProfiles;

internal sealed class Endpoint(
    ListPositionProfilesHandler handler) : Endpoint<ListPositionProfilesRequest, ListPositionProfilesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/position-profiles");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        ListPositionProfilesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
