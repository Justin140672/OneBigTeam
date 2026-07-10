using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetApplicationsByStatus;

internal sealed class Endpoint(
    GetApplicationsByStatusHandler handler) : Endpoint<GetApplicationsByStatusRequest, GetApplicationsByStatusResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/recruitment/applications");
        Policies("candidate:view");
    }

    public override async Task HandleAsync(
        GetApplicationsByStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
