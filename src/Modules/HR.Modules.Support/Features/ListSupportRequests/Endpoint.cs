using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Features.ListSupportRequests;

internal sealed class Endpoint(ListSupportRequestsHandler handler)
    : Endpoint<ListSupportRequestsRequest, List<ListSupportRequestsResponse>>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/support/requests");
        Policies("support:manage");
    }

    public override async Task HandleAsync(ListSupportRequestsRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
