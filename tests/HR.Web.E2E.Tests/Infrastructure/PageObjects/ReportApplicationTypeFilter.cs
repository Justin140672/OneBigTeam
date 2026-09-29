using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Internal recruitment Ticket 6: the "Applications" (All applications / Internal / External) filter
/// shared by the Recruitment Pipeline, Vacancy Performance and Recruitment Pipeline Summary report
/// pages. Each page renders it as an SfDropDownList inside
/// <c>[data-testid='report-application-type-filter']</c>, outside the loading/error/grid branches, so
/// the selection survives the reload it triggers. Selection always goes through
/// <see cref="DropDownSelector"/>.
/// </summary>
internal static class ReportApplicationTypeFilter
{
    public const string AllApplications = "All applications";
    public const string Internal = "Internal";
    public const string External = "External";

    private static ILocator Wrapper(IPage page) => page.Locator("[data-testid='report-application-type-filter']");

    public static Task SelectAsync(IPage page, string label) =>
        DropDownSelector.SelectAsync(page, Wrapper(page), label);

    public static Task ExpectSelectedAsync(IPage page, string label) =>
        Assertions.Expect(Wrapper(page).Locator("span[role='combobox'] input").First)
            .ToHaveValueAsync(label, new() { Timeout = 15_000 });

    public static async Task ExpectGridRenderedWithoutErrorAsync(IPage page)
    {
        await Assertions.Expect(page.Locator(".hr-loading")).ToHaveCountAsync(0, new() { Timeout = 30_000 });
        await Assertions.Expect(page.Locator(".e-grid .e-row, .e-grid .e-emptyrow").First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(page.Locator(".alert-danger")).ToHaveCountAsync(0);
    }
}
