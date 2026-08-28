using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.SearchApplications;

internal sealed class Endpoint(
    SearchApplicationsHandler handler) : Endpoint<SearchApplicationsRequest, SearchApplicationsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/recruitment/applications/search");
        Policies("candidate:view");
    }

    public override async Task HandleAsync(
        SearchApplicationsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
