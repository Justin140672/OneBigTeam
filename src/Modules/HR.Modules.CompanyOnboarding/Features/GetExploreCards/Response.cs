namespace HR.Modules.CompanyOnboarding.Features.GetExploreCards;

internal sealed record ExploreCardResponse(
    string Name,
    string Description,
    string LinkUrl,
    bool IsComingSoon);

internal sealed record GetExploreCardsResponse(IReadOnlyList<ExploreCardResponse> Cards);
