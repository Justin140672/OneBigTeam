using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.CompanyOnboarding.Features.MarkOnboardingTaskComplete;

internal sealed class Endpoint(
    MarkOnboardingTaskCompleteHandler handler) : Endpoint<MarkOnboardingTaskCompleteRequest, MarkOnboardingTaskCompleteResponse>
{
    public override void Configure()
    {
        Post("/api/company-onboarding/checklist/tasks/{TaskKey}/mark-complete");
        Policies("onboarding:manage");
    }

    public override async Task HandleAsync(MarkOnboardingTaskCompleteRequest req, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(req, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "unauthorized")
            {
                await Send.ResultAsync(TypedResults.Unauthorized());
                return;
            }

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
