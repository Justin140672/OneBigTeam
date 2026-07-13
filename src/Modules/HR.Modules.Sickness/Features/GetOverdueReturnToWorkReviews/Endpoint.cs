using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.GetOverdueReturnToWorkReviews;

internal sealed class Endpoint(
    GetOverdueReturnToWorkReviewsHandler handler) : Endpoint<GetOverdueReturnToWorkReviewsRequest, GetOverdueReturnToWorkReviewsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/return-to-work-reviews/overdue");
        // "sickness:review" (Manager + HrAdministrator) rather than "sickness:manage"
        // (HrAdministrator only) — this company-wide read is what backs
        // OverdueReturnToWorkReviewsWidget, shown on both the HR and Manager dashboards.
        Policies("sickness:review");
    }

    public override async Task HandleAsync(
        GetOverdueReturnToWorkReviewsRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
