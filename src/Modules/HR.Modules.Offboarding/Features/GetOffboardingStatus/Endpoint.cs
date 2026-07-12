using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Offboarding.Features.GetOffboardingStatus;

internal sealed class Endpoint(GetOffboardingStatusHandler handler)
    : Endpoint<GetOffboardingStatusRequest, GetOffboardingStatusResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/offboarding-status");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        GetOffboardingStatusRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
