using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.CompanyOnboarding.Features.GetOnboardingChecklist;

internal sealed class Endpoint(
    GetOnboardingChecklistHandler handler) : EndpointWithoutRequest<GetOnboardingChecklistResponse>
{
    public override void Configure()
    {
        Get("/api/company-onboarding/checklist");
        Policies("onboarding:view");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "unauthorized")
            {
                await Send.ResultAsync(TypedResults.Unauthorized());
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
