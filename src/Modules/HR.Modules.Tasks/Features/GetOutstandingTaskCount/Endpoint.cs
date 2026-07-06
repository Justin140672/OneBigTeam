using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.GetOutstandingTaskCount;

internal sealed class Endpoint(GetOutstandingTaskCountHandler handler)
    : Endpoint<GetOutstandingTaskCountRequest, GetOutstandingTaskCountResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/tasks/outstanding-count");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(GetOutstandingTaskCountRequest request, CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response.Value!));
    }
}
