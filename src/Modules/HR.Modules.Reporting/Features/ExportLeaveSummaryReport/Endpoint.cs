using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportLeaveSummaryReport;

internal sealed class Endpoint(
    ExportLeaveSummaryReportHandler handler,
    IAuthorizationService authorizationService,
    HR.SharedKernel.ICurrentUser currentUser) : Endpoint<ExportLeaveSummaryReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/leave-summary/export");
        Policies("reporting:view-leave-summary");
    }

    public override async Task HandleAsync(
        ExportLeaveSummaryReportRequest request,
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
