using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.GetUpcomingProbationReviews;

internal sealed class Endpoint(
    GetUpcomingProbationReviewsHandler handler) : Endpoint<GetUpcomingProbationReviewsRequest, GetUpcomingProbationReviewsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/probation-reviews/upcoming");
        // "probation:review" (Manager + HrAdministrator) rather than "probation:manage"
        // (HrAdministrator only) — this company-wide read is what backs
        // UpcomingProbationReviewsWidget, shown on both the HR and Manager dashboards.
        Policies("probation:review");
    }

    public override async Task HandleAsync(
        GetUpcomingProbationReviewsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
