using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.UpdateOnboardingTemplate;

internal sealed class Endpoint(UpdateOnboardingTemplateHandler handler)
    : Endpoint<UpdateOnboardingTemplateRequest, UpdateOnboardingTemplateResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/onboarding-templates/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        UpdateOnboardingTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
