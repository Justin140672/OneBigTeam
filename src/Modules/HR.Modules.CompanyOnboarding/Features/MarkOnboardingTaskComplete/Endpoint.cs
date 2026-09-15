using FastEndpoints;
using HR.SharedKernel;
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
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
