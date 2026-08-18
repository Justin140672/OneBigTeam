using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the new static marketing page at /coming-soon (ComingSoon.razor), including the
/// header/footer nav links added to reach it, and the "Coming soon" / "Planned" grouped
/// feature-card sections sourced from UpcomingFeatureCatalog.
/// </summary>
public sealed class ComingSoonPageTests(ParallelBlankPersonaFixture fixture)
    : RoleE2ETestBase<ParallelBlankPersonaFixture>(fixture)
{
    [Fact]
    public async Task DirectNavigation_LoadsPage_WithHeroHeadingAndBody()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/coming-soon");

        await Assertions.Expect(_page.Locator("section.pricing-hero h1"))
            .ToHaveTextAsync("More is coming to One Big Team");

        await Assertions.Expect(_page.Locator("section.pricing-hero"))
            .ToContainTextAsync(
                "We're continually improving One Big Team. Here's a look at some of the features we're planning next.");
    }

    [Fact]
    public async Task HeaderComingSoonLink_NavigatesToComingSoonPage()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/");

        var headerLink = _page.Locator("nav#site-navigation a", new() { HasText = "Coming Soon" });
        Assert.EndsWith("/coming-soon", await headerLink.GetAttributeAsync("href"));

        await headerLink.ClickAsync();

        await _page.WaitForURLAsync(new Regex("/coming-soon"), new() { Timeout = 20_000 });
        Assert.Contains("/coming-soon", _page.Url);

        await Assertions.Expect(_page.Locator("section.pricing-hero h1"))
            .ToHaveTextAsync("More is coming to One Big Team");
    }

    [Fact]
    public async Task FooterComingSoonLink_NavigatesToComingSoonPage()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/");

        var footerLink = _page.Locator(".footer-links a", new() { HasText = "Coming Soon" });
        Assert.EndsWith("/coming-soon", await footerLink.GetAttributeAsync("href"));

        await footerLink.ClickAsync();

        await _page.WaitForURLAsync(new Regex("/coming-soon"), new() { Timeout = 20_000 });
        Assert.Contains("/coming-soon", _page.Url);

        await Assertions.Expect(_page.Locator("section.pricing-hero h1"))
            .ToHaveTextAsync("More is coming to One Big Team");
    }

    [Fact]
    public async Task ComingSoonSection_RendersExpectedCards()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/coming-soon");

        var comingSoonSection = _page.Locator(
            "section.section",
            new() { Has = _page.Locator(".section-heading h2", new() { HasText = "Coming soon" }) });
        await Assertions.Expect(comingSoonSection).ToBeVisibleAsync();

        var positionProfilesCard = comingSoonSection.Locator(".card", new() { HasText = "AI-powered position profiles" });
        await Assertions.Expect(positionProfilesCard.Locator("h3")).ToHaveTextAsync("AI-powered position profiles");
        await Assertions.Expect(positionProfilesCard.Locator("p"))
            .ToContainTextAsync("Generate and improve job descriptions and position profiles using AI.");

        var webhooksCard = comingSoonSection.Locator(".card", new() { HasText = "Employee webhooks" });
        await Assertions.Expect(webhooksCard.Locator("h3")).ToHaveTextAsync("Employee webhooks");
        await Assertions.Expect(webhooksCard.Locator("p"))
            .ToContainTextAsync("Integrate One Big Team with other systems when employees join, change or leave.");
    }

    [Fact]
    public async Task PlannedSection_RendersExpectedCard()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/coming-soon");

        var plannedSection = _page.Locator(
            "section.section",
            new() { Has = _page.Locator(".section-heading h2", new() { HasText = "Planned" }) });
        await Assertions.Expect(plannedSection).ToBeVisibleAsync();

        var aiAssistantCard = plannedSection.Locator(".card", new() { HasText = "AI help assistant" });
        await Assertions.Expect(aiAssistantCard.Locator("h3")).ToHaveTextAsync("AI help assistant");
        await Assertions.Expect(aiAssistantCard.Locator("p"))
            .ToContainTextAsync("Ask questions about One Big Team and get contextual help.");
    }

    [Fact]
    public async Task StatusSections_AreGrouped_WithComingSoonAboveThePlannedSection()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/coming-soon");

        var comingSoonHeading = _page.Locator(".section-heading h2", new() { HasText = "Coming soon" });
        var plannedHeading = _page.Locator(".section-heading h2", new() { HasText = "Planned" });

        await Assertions.Expect(comingSoonHeading).ToBeVisibleAsync();
        await Assertions.Expect(plannedHeading).ToBeVisibleAsync();

        var comingSoonBox = await comingSoonHeading.BoundingBoxAsync();
        var plannedBox = await plannedHeading.BoundingBoxAsync();

        Assert.NotNull(comingSoonBox);
        Assert.NotNull(plannedBox);
        Assert.True(plannedBox!.Y > comingSoonBox!.Y);
    }
}
