using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.RemoveOnboardingTemplateFromPositionProfile;

internal sealed class Endpoint(RemoveOnboardingTemplateHandler handler, ICurrentUser currentUser) : Endpoint<RemoveOnboardingTemplateRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/position-profiles/{positionProfileId:guid}/onboarding-templates/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        RemoveOnboardingTemplateRequest request,
        CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } actorEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, actorEmployeeId, cancellationToken);

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
