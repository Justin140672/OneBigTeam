using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetSubscriptionDetails;

internal sealed class Endpoint(
    GetSubscriptionDetailsHandler handler) : EndpointWithoutRequest<GetSubscriptionDetailsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/subscription-details");
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
