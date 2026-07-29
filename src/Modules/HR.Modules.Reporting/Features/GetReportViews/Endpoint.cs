using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetReportViews;

internal sealed class Endpoint(GetReportViewsHandler handler, ICurrentUser currentUser)
    : Endpoint<GetReportViewsRequest, GetReportViewsResponse>
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

        var result = await handler.HandleAsync(request, userId, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
