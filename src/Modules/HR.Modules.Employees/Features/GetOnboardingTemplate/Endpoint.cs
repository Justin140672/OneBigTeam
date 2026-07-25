using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetOnboardingTemplate;

internal sealed class Endpoint(GetOnboardingTemplateHandler handler)
    : Endpoint<GetOnboardingTemplateRequest, GetOnboardingTemplateResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/onboarding-templates/{id:guid}");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetOnboardingTemplateRequest request,
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
