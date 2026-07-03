using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.GetOverdueReturnToWorkReviews;

internal sealed class Endpoint(
    GetOverdueReturnToWorkReviewsHandler handler) : Endpoint<GetOverdueReturnToWorkReviewsRequest, GetOverdueReturnToWorkReviewsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/return-to-work-reviews/overdue");
        Policies("sickness:manage");
    }

    public override async Task HandleAsync(
        GetOverdueReturnToWorkReviewsRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
