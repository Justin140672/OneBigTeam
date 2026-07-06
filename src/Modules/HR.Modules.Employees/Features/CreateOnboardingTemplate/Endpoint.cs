using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.CreateOnboardingTemplate;

internal sealed class Endpoint(CreateOnboardingTemplateHandler handler)
    : Endpoint<CreateOnboardingTemplateRequest, CreateOnboardingTemplateResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/onboarding-templates");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        CreateOnboardingTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/onboarding-templates/{result.Value.Id}",
            result.Value));
    }
}
