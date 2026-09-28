using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.GetOutstandingTaskCount;

internal sealed class Endpoint(GetOutstandingTaskCountHandler handler)
    : Endpoint<GetOutstandingTaskCountRequest, GetOutstandingTaskCountResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/tasks/outstanding-count");
        // Sole caller is RecruitmentSummaryWidget, querying Source=Recruitment counts for a
        // Recruiter persona — Recruiter does not hold employee:manage (see
        // RolePermissionConfiguration), so that policy 403'd this endpoint for every real
        // Recruiter-only user. recruitment:manage matches the widget's actual caller and its
        // sibling metric endpoint (GetInterviewsTodayCount) in the same widget.
        // Ticket 7: migrated from candidate:view (removed as redundant).
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(GetOutstandingTaskCountRequest request, CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response.Value!));
    }
}
