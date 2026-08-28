using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.GetAccessReview;

// IAM-08: access-review report — every user carrying a privileged role (beyond the baseline
// Employee role), with the source of each privilege (direct role, inherited/position role, or
// override). Gated by "users:manage" (not "users:view") since this surfaces the company's full
// privileged-access map in one place, a materially more sensitive view than any single user's
// record.
internal sealed class Endpoint(GetAccessReviewHandler handler) : Endpoint<GetAccessReviewRequest, GetAccessReviewResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/users/access-review");
        Policies("users:manage");
    }

    public override async Task HandleAsync(GetAccessReviewRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
