using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers two marketing-site UI changes on the "marketing" Aspire resource:
///   1. Contact navigation consistency — SiteHeader's and SiteFooter's "Contact" links now point
///      at /contact#contact-form (instead of the bare /contact) so every Contact CTA site-wide
///      consistently lands on the visible contact form, not just the top of the page.
///   2. The new homepage trust/reassurance section (Home.razor), inserted between the hero and
///      the #features section, with its heading, four trust-card headings, and outbound links.
/// </summary>
[Collection("E2E")]
public sealed class MarketingContactAndTrustSectionTests(AppFixture fixture) : E2ETestBase(fixture)
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
    public async Task OnPageContactCta_NavigatesToContactFormAnchor()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/");

        // The final-CTA section's "Contact us" button (distinct from the header/footer links
        // already covered above).
        var contactCta = _page.Locator("section.final-cta a", new() { HasText = "Contact us" });
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
    public async Task HomepageTrustSection_RendersBetweenHeroAndFeatures_WithHeadingAndCards()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/");

        var trustSection = _page.Locator("section.trust-section");
        await Assertions.Expect(trustSection).ToBeVisibleAsync();

        await Assertions.Expect(_page.Locator("#trust-heading"))
            .ToHaveTextAsync("Your employee data, handled carefully.");

        var cardHeadings = new[]
        {
            "Your employee data, handled carefully",
            "Transparent from day one",
            "Help when you need it",
            "Try it without commitment",
        };

        foreach (var heading in cardHeadings)
        {
            await Assertions.Expect(
                _page.Locator(".trust-card h3", new() { HasText = heading })).ToBeVisibleAsync();
        }

        // Positioned below the hero section and above the #features section.
        var heroBox = await _page.Locator("section.hero").BoundingBoxAsync();
        var trustBox = await trustSection.BoundingBoxAsync();
        var featuresBox = await _page.Locator("section#features").BoundingBoxAsync();

        Assert.NotNull(heroBox);
        Assert.NotNull(trustBox);
        Assert.NotNull(featuresBox);
        Assert.True(trustBox!.Y >= heroBox!.Y);
        Assert.True(featuresBox!.Y >= trustBox.Y);
    }

    [Fact]
    public async Task HomepageTrustSection_CardLinks_PointAtExpectedDestinations()
    {
        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/");

        var trustSection = _page.Locator("section.trust-section");

        var privacyLink = trustSection.Locator("a[href='/privacy-policy']").First;
        var securityLink = trustSection.Locator("a[href='/security']").First;
        var contactLink = trustSection.Locator("a[href='/contact#contact-form']").First;
        var pricingLink = trustSection.Locator("a[href='/pricing']").First;

        await Assertions.Expect(privacyLink).ToBeVisibleAsync();
        await Assertions.Expect(securityLink).ToBeVisibleAsync();
        await Assertions.Expect(contactLink).ToBeVisibleAsync();
        await Assertions.Expect(pricingLink).ToBeVisibleAsync();
    }
}
