using FastEndpoints;
using HR.Modules.Sickness.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.GetOverdueReturnToWorkReviews;

internal sealed class Endpoint(
    GetOverdueReturnToWorkReviewsHandler handler,
    ICurrentUser currentUser,
    SicknessResourceAuthorizer authorizer) : Endpoint<GetOverdueReturnToWorkReviewsRequest, GetOverdueReturnToWorkReviewsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/return-to-work-reviews/overdue");
        // "sickness:review" (Manager + HrAdministrator) rather than "sickness:manage"
        // (HrAdministrator only) — this read is what backs OverdueReturnToWorkReviewsWidget,
        // shown on both the HR and Manager dashboards. The policy only proves role membership;
        // SICK-02 scopes the actual rows returned to the caller's reporting hierarchy (or
        // company-wide for HR).
        Policies("sickness:review");
    }

    public override async Task HandleAsync(
        GetOverdueReturnToWorkReviewsRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var authorizedEmployeeIds = await authorizer.GetAuthorizedEmployeeIdsAsync(
            request.CompanyId, callerId, cancellationToken);

        var response = await handler.HandleAsync(request, authorizedEmployeeIds, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
