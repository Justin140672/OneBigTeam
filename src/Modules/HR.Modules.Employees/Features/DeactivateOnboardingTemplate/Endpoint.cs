using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.DeactivateOnboardingTemplate;

internal sealed class Endpoint(DeactivateOnboardingTemplateHandler handler)
    : Endpoint<DeactivateOnboardingTemplateRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/onboarding-templates/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        DeactivateOnboardingTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
