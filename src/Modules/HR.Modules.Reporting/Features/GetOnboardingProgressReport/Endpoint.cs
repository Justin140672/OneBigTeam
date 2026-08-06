using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetOnboardingProgressReport;

internal sealed class Endpoint(
    GetOnboardingProgressReportHandler handler,
    IAuthorizationService authorizationService,
    HR.SharedKernel.ICurrentUser currentUser) : Endpoint<GetOnboardingProgressReportRequest, GetOnboardingProgressReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/onboarding-progress");
        // Manager OR HrAdministrator baseline access — the handler enforces row-level scoping down
        // to direct reports for non-HR callers (see Handler.cs), mirroring GetProbationReport.
        Policies("reporting:view-onboarding");
    }

    public override async Task HandleAsync(
        GetOnboardingProgressReportRequest request,
        CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } callerEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var callerIsHr = (await authorizationService.AuthorizeAsync(User, "reporting:view-hr")).Succeeded;

        var result = await handler.HandleAsync(request, callerIsHr, callerEmployeeId, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
