using FastEndpoints;
using HR.Modules.Onboarding.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Onboarding.Features.GetTeamOnboarding;

internal sealed class Endpoint(
    GetTeamOnboardingHandler handler,
    ICurrentUser currentUser,
    OnboardingResourceAuthorizer resourceAuthorizer)
    : Endpoint<GetTeamOnboardingRequest, GetTeamOnboardingResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{managerId:guid}/team-onboarding");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetTeamOnboardingRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!await resourceAuthorizer.CanViewManagerTeamAsync(
                request.CompanyId, callerEmployeeId, request.ManagerId, cancellationToken))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
