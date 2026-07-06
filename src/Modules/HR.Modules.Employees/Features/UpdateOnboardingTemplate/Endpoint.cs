using FastEndpoints;
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
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
