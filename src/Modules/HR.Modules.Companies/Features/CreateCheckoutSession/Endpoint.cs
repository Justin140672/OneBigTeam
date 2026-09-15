using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.CreateCheckoutSession;

internal sealed class Endpoint(
    CreateCheckoutSessionHandler handler) : EndpointWithoutRequest<CreateCheckoutSessionResponse>
{
    public override void Configure()
    {
        Post("/api/companies/checkout-session");
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
