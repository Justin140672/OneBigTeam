using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.CreateBillingPortalSession;

internal sealed class Endpoint(
    CreateBillingPortalSessionHandler handler) : EndpointWithoutRequest<CreateBillingPortalSessionResponse>
{
    public override void Configure()
    {
        Post("/api/companies/subscription/billing-portal");
        Policies("subscription:manage");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
