using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetLeaveSummaryReport;

internal sealed class Endpoint(
    GetLeaveSummaryReportHandler handler,
    IAuthorizationService authorizationService,
    HR.SharedKernel.ICurrentUser currentUser) : Endpoint<GetLeaveSummaryReportRequest, GetLeaveSummaryReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/leave-summary");
        Policies("reporting:view-leave-summary");
    }

    public override async Task HandleAsync(
        GetLeaveSummaryReportRequest request,
        CancellationToken cancellationToken)
    {
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
