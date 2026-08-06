using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportOnboardingProgressReport;

internal sealed class Endpoint(
    ExportOnboardingProgressReportHandler handler,
    IAuthorizationService authorizationService,
    HR.SharedKernel.ICurrentUser currentUser) : Endpoint<ExportOnboardingProgressReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/onboarding-progress/export");
        Policies("reporting:view-onboarding");
    }

    public override async Task HandleAsync(
        ExportOnboardingProgressReportRequest request,
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
        var file = result.Value!.File;

        await Send.BytesAsync(
            file.Content,
            fileName: file.FileName,
            contentType: file.ContentType,
            cancellation: cancellationToken);
    }
}
