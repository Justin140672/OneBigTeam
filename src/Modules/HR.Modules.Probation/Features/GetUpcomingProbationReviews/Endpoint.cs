using FastEndpoints;
using HR.Modules.Probation.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.GetUpcomingProbationReviews;

internal sealed class Endpoint(
    GetUpcomingProbationReviewsHandler handler,
    ICurrentUser currentUser,
    ProbationResourceAuthorizer authorizer) : Endpoint<GetUpcomingProbationReviewsRequest, GetUpcomingProbationReviewsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/probation-reviews/upcoming");
        Policies("probation:review");
    }

    public override async Task HandleAsync(
        GetUpcomingProbationReviewsRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var authorizedEmployeeIds = await authorizer.GetAuthorizedEmployeeIdsAsync(
            request.CompanyId, callerId, cancellationToken);

        var result = await handler.HandleAsync(request, authorizedEmployeeIds, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
