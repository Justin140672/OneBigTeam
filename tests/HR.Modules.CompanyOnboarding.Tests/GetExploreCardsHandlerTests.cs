using HR.Modules.CompanyOnboarding.Features.GetExploreCards;

namespace HR.Modules.CompanyOnboarding.Tests;

public class GetExploreCardsHandlerTests
{
    [Fact]
    public async Task HandleAsync_Returns_Six_Static_Cards_With_Reports_Marked_ComingSoon()
    {
        var handler = new GetExploreCardsHandler();

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(6, result.Value!.Cards.Count);

        var reports = Assert.Single(result.Value.Cards, c => c.Name == "Reports");
        Assert.True(reports.IsComingSoon);

        Assert.All(result.Value.Cards.Where(c => c.Name != "Reports"), c => Assert.False(c.IsComingSoon));
    }
}
