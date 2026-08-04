using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.CompanyOnboarding.Features.GetExploreCards;

internal sealed class Endpoint(
    GetExploreCardsHandler handler) : EndpointWithoutRequest<GetExploreCardsResponse>
{
    public override void Configure()
    {
        Get("/api/company-onboarding/explore-cards");
        Policies("onboarding:view");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
