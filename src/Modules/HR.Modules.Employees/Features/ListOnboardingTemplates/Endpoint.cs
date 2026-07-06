using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ListOnboardingTemplates;

internal sealed class Endpoint(ListOnboardingTemplatesHandler handler)
    : Endpoint<ListOnboardingTemplatesRequest, ListOnboardingTemplatesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/onboarding-templates");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        ListOnboardingTemplatesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
