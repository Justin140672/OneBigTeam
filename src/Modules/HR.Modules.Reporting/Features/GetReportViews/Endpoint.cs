using FastEndpoints;
using HR.Modules.Reporting.ReportRegistry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetReportViews;

internal sealed class Endpoint(
    GetReportViewsHandler handler,
    HR.SharedKernel.ICurrentUser currentUser,
    IAuthorizationService authorizationService) : Endpoint<GetReportViewsRequest, GetReportViewsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/saved-views/{reportId}");
        Policies("reporting:view");
    }

    public override async Task HandleAsync(
        GetReportViewsRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var accessGates = await ReportAccessGateEvaluator.EvaluateAsync(authorizationService, User);

        var result = await handler.HandleAsync(request, userId, accessGates, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
