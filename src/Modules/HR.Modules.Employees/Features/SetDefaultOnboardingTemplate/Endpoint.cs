using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.SetDefaultOnboardingTemplate;

internal sealed class Endpoint(SetDefaultOnboardingTemplateHandler handler)
    : Endpoint<SetDefaultOnboardingTemplateRequest>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/onboarding-templates/{id:guid}/set-default");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        SetDefaultOnboardingTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
