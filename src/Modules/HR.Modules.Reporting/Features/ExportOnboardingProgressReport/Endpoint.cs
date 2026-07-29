using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportOnboardingProgressReport;

internal sealed class Endpoint(
    ExportOnboardingProgressReportHandler handler,
    IAuthorizationService authorizationService) : Endpoint<ExportOnboardingProgressReportRequest>
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
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var callerEmployeeId))
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
