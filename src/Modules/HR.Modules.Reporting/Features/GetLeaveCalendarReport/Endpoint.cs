using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetLeaveCalendarReport;

internal sealed class Endpoint(GetLeaveCalendarReportHandler handler)
    : Endpoint<GetLeaveCalendarReportRequest, GetLeaveCalendarReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/leave-calendar");
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        GetLeaveCalendarReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
