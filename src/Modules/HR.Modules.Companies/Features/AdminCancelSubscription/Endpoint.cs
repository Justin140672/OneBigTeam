using FastEndpoints;

using HR.SharedKernel;

using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.AdminCancelSubscription;

internal sealed class Endpoint(
    AdminCancelSubscriptionHandler handler) : Endpoint<AdminCancelSubscriptionRequest, AdminCancelSubscriptionResponse>
{
    public override void Configure()
    {
        Post("/api/companies/admin/customers/{companyId:guid}/subscription/cancel");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(AdminCancelSubscriptionRequest req, CancellationToken cancellationToken)
    {
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            req with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
