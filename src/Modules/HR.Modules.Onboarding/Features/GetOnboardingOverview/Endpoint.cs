using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Onboarding.Features.GetOnboardingOverview;

internal sealed class Endpoint(GetOnboardingOverviewHandler handler)
    : Endpoint<GetOnboardingOverviewRequest, GetOnboardingOverviewResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/onboarding-overview");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        GetOnboardingOverviewRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
