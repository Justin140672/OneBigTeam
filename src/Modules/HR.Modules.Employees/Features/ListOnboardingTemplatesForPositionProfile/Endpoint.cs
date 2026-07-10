using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ListOnboardingTemplatesForPositionProfile;

internal sealed class Endpoint(ListOnboardingTemplatesForPositionProfileHandler handler)
    : Endpoint<ListOnboardingTemplatesForPositionProfileRequest, ListOnboardingTemplatesForPositionProfileResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/position-profiles/{positionProfileId:guid}/onboarding-templates");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        ListOnboardingTemplatesForPositionProfileRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
