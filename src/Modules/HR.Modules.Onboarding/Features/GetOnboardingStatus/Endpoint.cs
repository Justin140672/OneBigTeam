using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Onboarding.Features.GetOnboardingStatus;

internal sealed class Endpoint(GetOnboardingStatusHandler handler)
    : Endpoint<GetOnboardingStatusRequest, GetOnboardingStatusResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/onboarding-status");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        GetOnboardingStatusRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
