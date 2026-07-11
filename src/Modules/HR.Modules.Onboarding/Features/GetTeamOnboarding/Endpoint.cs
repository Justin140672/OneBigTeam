using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Onboarding.Features.GetTeamOnboarding;

internal sealed class Endpoint(GetTeamOnboardingHandler handler)
    : Endpoint<GetTeamOnboardingRequest, GetTeamOnboardingResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{managerId:guid}/team-onboarding");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        GetTeamOnboardingRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
