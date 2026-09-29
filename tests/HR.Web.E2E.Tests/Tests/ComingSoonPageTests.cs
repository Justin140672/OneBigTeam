using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class ComingSoonPageTests(ParallelBlankPersonaFixture fixture)
    : RoleE2ETestBase<ParallelBlankPersonaFixture>(fixture)
{
    [Fact]
    public async Task DirectNavigation_LoadsPage_WithHeroHeadingAndBody()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/roadmap");

        await Assertions.Expect(_page.Locator("section.pricing-hero h1"))
            .ToHaveTextAsync("Product Roadmap");

        await Assertions.Expect(_page.Locator("section.pricing-hero"))
            .ToContainTextAsync(
                "We're continually improving One Big Team. Here's a look at some of the problems we're working on solving next.");
    }

    [Fact]
    public async Task LegacyComingSoonUrl_RedirectsToRoadmap()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/coming-soon");

        await _page.WaitForURLAsync(new Regex("/roadmap"), new() { Timeout = 20_000 });
        Assert.Contains("/roadmap", _page.Url);

        await Assertions.Expect(_page.Locator("section.pricing-hero h1"))
            .ToHaveTextAsync("Product Roadmap");
    }

    [Fact]
    public async Task RoadmapPage_ShowsDisclaimer()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/roadmap");

        await Assertions.Expect(_page.Locator(".roadmap-disclaimer"))
            .ToContainTextAsync("Plans may change");
    }

    [Fact]
    public async Task FooterRoadmapLink_NavigatesToRoadmapPage()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/");

        var footerLink = _page.Locator(".footer-links a", new() { HasText = "Roadmap" });
        Assert.EndsWith("/roadmap", await footerLink.GetAttributeAsync("href"));

        await footerLink.ClickAsync();

        await _page.WaitForURLAsync(new Regex("/roadmap"), new() { Timeout = 20_000 });
        Assert.Contains("/roadmap", _page.Url);

        await Assertions.Expect(_page.Locator("section.pricing-hero h1"))
            .ToHaveTextAsync("Product Roadmap");
    }

    [Fact]
    public async Task ComingSoonSection_RendersExpectedCards()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/roadmap");

        var comingSoonSection = _page.Locator(
            "section.section",
            new() { Has = _page.Locator(".section-heading h2", new() { HasText = "Coming soon" }) });
        await Assertions.Expect(comingSoonSection).ToBeVisibleAsync();

        var jobDescriptionsCard = comingSoonSection.Locator(".card", new() { HasText = "AI-generated Live Job Descriptions" });
        await Assertions.Expect(jobDescriptionsCard.Locator("h3")).ToHaveTextAsync("AI-generated Live Job Descriptions");

        var webhooksCard = comingSoonSection.Locator(".card", new() { HasText = "Webhooks & Integrations" });
        await Assertions.Expect(webhooksCard.Locator("h3")).ToHaveTextAsync("Webhooks & Integrations");

        var aiAssistantCard = comingSoonSection.Locator(".card", new() { HasText = "AI Help Assistant" });
        await Assertions.Expect(aiAssistantCard.Locator("h3")).ToHaveTextAsync("AI Help Assistant");
    }

    [Fact]
    public async Task RoadmapPage_HasExactlyOneComingSoonSection_AndNoPlannedSection()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/roadmap");

        var comingSoonHeading = _page.Locator(".section-heading h2", new() { HasText = "Coming soon" });
        await Assertions.Expect(comingSoonHeading).ToBeVisibleAsync();
        await Assertions.Expect(comingSoonHeading).ToHaveCountAsync(1);

        var plannedHeading = _page.Locator(".section-heading h2", new() { HasText = "Planned" });
        await Assertions.Expect(plannedHeading).ToHaveCountAsync(0);
    }
}
