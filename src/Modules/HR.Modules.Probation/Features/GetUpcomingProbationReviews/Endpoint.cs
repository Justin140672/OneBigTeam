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
        // "probation:review" (Manager + HrAdministrator) rather than "probation:manage"
        // (HrAdministrator only) — this read is what backs UpcomingProbationReviewsWidget, shown
        // on both the HR and Manager dashboards. The policy only proves role membership; PROB-02
        // scopes the actual rows returned to the caller's reporting hierarchy (or company-wide
        // for HR).
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
