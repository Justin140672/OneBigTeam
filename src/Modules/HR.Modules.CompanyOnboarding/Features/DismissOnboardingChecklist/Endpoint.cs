using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.CompanyOnboarding.Features.DismissOnboardingChecklist;

internal sealed class Endpoint(
    DismissOnboardingChecklistHandler handler) : EndpointWithoutRequest<DismissOnboardingChecklistResponse>
{
    public override void Configure()
    {
        Post("/api/company-onboarding/checklist/dismiss");
        Policies("onboarding:manage");
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
