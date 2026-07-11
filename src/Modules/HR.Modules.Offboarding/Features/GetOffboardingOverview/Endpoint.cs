using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Offboarding.Features.GetOffboardingOverview;

internal sealed class Endpoint(GetOffboardingOverviewHandler handler)
    : Endpoint<GetOffboardingOverviewRequest, GetOffboardingOverviewResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/offboarding-overview");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        GetOffboardingOverviewRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
