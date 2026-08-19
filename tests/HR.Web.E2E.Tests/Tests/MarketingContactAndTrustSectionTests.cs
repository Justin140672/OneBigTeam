using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers two marketing-site UI changes on the "marketing" Aspire resource:
///   1. Contact navigation consistency — SiteHeader's and SiteFooter's "Contact" links now point
///      at /contact#contact-form (instead of the bare /contact) so every Contact CTA site-wide
///      consistently lands on the visible contact form, not just the top of the page.
///   2. The homepage's compact trust/security section (Home.razor), with its heading, summary
///      copy and outbound links to Security and Privacy Policy.
/// </summary>
public sealed class MarketingContactAndTrustSectionTests(ParallelBlankPersonaFixture fixture)
    : RoleE2ETestBase<ParallelBlankPersonaFixture>(fixture)
{
    [Fact]
    public async Task HeaderContactLink_NavigatesToContactFormAnchor()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/");

        var headerContactLink = _page.Locator("nav#site-navigation a", new() { HasText = "Contact" });
        Assert.EndsWith("/contact#contact-form", await headerContactLink.GetAttributeAsync("href"));

        await headerContactLink.ClickAsync();

        await _page.WaitForURLAsync(new Regex("/contact#contact-form"), new() { Timeout = 20_000 });
        Assert.Contains("/contact#contact-form", _page.Url);

        await Assertions.Expect(_page.Locator("#contact-form")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task FooterContactLink_NavigatesToContactFormAnchor()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/");

        var footerContactLink = _page.Locator(".footer-links a", new() { HasText = "Contact" });
        Assert.EndsWith("/contact#contact-form", await footerContactLink.GetAttributeAsync("href"));

        await footerContactLink.ClickAsync();

        await _page.WaitForURLAsync(new Regex("/contact#contact-form"), new() { Timeout = 20_000 });
        Assert.Contains("/contact#contact-form", _page.Url);

        await Assertions.Expect(_page.Locator("#contact-form")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TrustSectionContactCta_NavigatesToContactFormAnchor()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/");

        // The trust section's "get in touch" link (distinct from the header/footer links
        // already covered above).
        var contactCta = _page.Locator("section.trust-section a", new() { HasText = "get in touch" });
        Assert.EndsWith("/contact#contact-form", await contactCta.GetAttributeAsync("href"));

        await contactCta.ClickAsync();

        await _page.WaitForURLAsync(new Regex("/contact#contact-form"), new() { Timeout = 20_000 });
        Assert.Contains("/contact#contact-form", _page.Url);

        await Assertions.Expect(_page.Locator("#contact-form")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ContactPage_ContactForm_IsVisibleAfterDirectNavigation()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/contact#contact-form");

        await Assertions.Expect(_page.Locator("#contact-form")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task HomepageTrustSection_RendersAfterFeaturesVideoAndSetupSteps_WithHeading()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/");

        var trustSection = _page.Locator("section.trust-section");
        await Assertions.Expect(trustSection).ToBeVisibleAsync();

        await Assertions.Expect(_page.Locator("#trust-heading"))
            .ToHaveTextAsync("Your employee data, handled carefully.");

        // Positioned below the #features, #watch and setup-timeline sections.
        var featuresBox = await _page.Locator("section#features").BoundingBoxAsync();
        var watchBox = await _page.Locator("section#watch").BoundingBoxAsync();
        var timelineBox = await _page.Locator(".timeline-steps").BoundingBoxAsync();
        var trustBox = await trustSection.BoundingBoxAsync();

        Assert.NotNull(featuresBox);
        Assert.NotNull(watchBox);
        Assert.NotNull(timelineBox);
        Assert.NotNull(trustBox);
        Assert.True(watchBox!.Y >= featuresBox!.Y);
        Assert.True(timelineBox!.Y >= watchBox.Y);
        Assert.True(trustBox!.Y >= timelineBox.Y);
    }

    [Fact]
    public async Task HomepageSetupTimeline_RendersFourSteps()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/");

        await Assertions.Expect(_page.Locator(".section-heading h2", new() { HasText = "Set up your HR workspace in four clear steps." }))
            .ToBeVisibleAsync();

        await Assertions.Expect(_page.Locator(".timeline-steps li")).ToHaveCountAsync(4);
    }

    [Fact]
    public async Task HomepageTrustSection_Links_PointAtExpectedDestinations()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/");

        var trustSection = _page.Locator("section.trust-section");

        var privacyLink = trustSection.Locator("a[href='/privacy-policy']").First;
        var securityLink = trustSection.Locator("a[href='/security']").First;
        var contactLink = trustSection.Locator("a[href='/contact#contact-form']").First;

        await Assertions.Expect(privacyLink).ToBeVisibleAsync();
        await Assertions.Expect(securityLink).ToBeVisibleAsync();
        await Assertions.Expect(contactLink).ToBeVisibleAsync();
    }
}
