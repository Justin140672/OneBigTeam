using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.ListLeavePolicies;

internal sealed class Endpoint(
    ListLeavePoliciesHandler handler) : Endpoint<ListLeavePoliciesRequest, ListLeavePoliciesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/leave-policies");
        Policies("leave:approve");
    }

    public override async Task HandleAsync(
        ListLeavePoliciesRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
