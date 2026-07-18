using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetVacanciesNeedingPositionProfileReview;

internal sealed class Endpoint(GetVacanciesNeedingPositionProfileReviewHandler handler)
    : Endpoint<GetVacanciesNeedingPositionProfileReviewRequest, GetVacanciesNeedingPositionProfileReviewResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/vacancies/position-profile-matches/review");
        Policies("recruitment:view");
    }

    public override async Task HandleAsync(
        GetVacanciesNeedingPositionProfileReviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
