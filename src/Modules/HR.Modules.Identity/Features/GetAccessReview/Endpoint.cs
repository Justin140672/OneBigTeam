using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.GetAccessReview;

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
