using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Features.GetSupportDashboard;

internal sealed class Endpoint(GetSupportDashboardHandler handler)
    : EndpointWithoutRequest<GetSupportDashboardResponse>
{
    public override void Configure()
    {
        Get("/api/support/dashboard");
        Policies("support:manage");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
