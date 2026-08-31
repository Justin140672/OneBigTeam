using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// ADM-08 administrative governance reporting hub — report catalogue integration.
///
/// As an HR Administrator the catalogue (/companies/{companyId}/reporting — ReportCatalogPage.razor)
/// shows an "Administration" category containing the four governance report cards (User Activity,
/// Administrative Changes, Security Events, Compliance Status), each of which navigates to its
/// dedicated page and loads (grid + filters visible).
///
/// As a non-HR persona who still holds baseline reporting access (a Recruiter — reporting:view but
/// not reporting.view-governance), the "Administration" category and its cards are not rendered,
/// and direct navigation to a governance report route is redirected to /access-denied by
/// AppSession.GuardAccess — the same guard ReportCatalogPage-linked report pages use.
///
/// This file focuses on catalogue visibility and access control; per-report journey behaviour
/// (filters, export, favourites, saved views) lives in <see cref="GovernanceReportingJourneyTests"/>.
/// </summary>
public sealed class GovernanceReportingCatalogueTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator
    private const string MarcusEmail = "marcus.diallo@acme.example"; // Recruiter — has reporting:view, not governance

    public static IEnumerable<object[]> GovernanceCards =>
    [
        ["User Activity", "user-activity"],
        ["Administrative Changes", "administrative-changes"],
        ["Security Events", "security-events"],
        ["Compliance Status", "compliance-status"],
    ];

    [Fact]
    public async Task HrAdmin_Catalogue_ShowsAdministrationCategory_WithFourGovernanceCards()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catalog.GoToAsync(AcmeId);

        Assert.True(
            await _page.Locator("h5").GetByText("Administration", new() { Exact = true }).IsVisibleAsync(),
            "Expected an 'Administration' report category heading for an HR Administrator");

        foreach (var card in new[] { "User Activity", "Administrative Changes", "Security Events", "Compliance Status" })
        {
            Assert.True(await catalog.HasCardAsync(card), $"Expected the '{card}' governance card to be visible");
            Assert.True(await catalog.IsCardClickableAsync(card), $"Expected the '{card}' governance card to be clickable");
        }
    }

    [Theory]
    [MemberData(nameof(GovernanceCards))]
    public async Task HrAdmin_GovernanceCard_NavigatesToReportPage_ThatLoads(string cardTitle, string routeSegment)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catalog.GoToAsync(AcmeId);
        await catalog.ClickCardAsync(cardTitle);

        await _page.WaitForURLAsync($"**/reporting/governance/{routeSegment}", new() { Timeout = 15_000 });

        if (routeSegment == "compliance-status")
        {
            var report = new GovernanceComplianceStatusReportPage(_page, _fixture.WebBaseUrl);
            await _page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 20_000 });
            Assert.False(await report.HasLoadErrorAsync(), $"'{cardTitle}' report page reported a load error");
            Assert.True(await report.AreFiltersVisibleAsync(), "Expected the filters card to be visible");
            Assert.True(await report.IsGridVisibleAsync(), "Expected the report grid to be visible");
        }
        else
        {
            var report = new GovernanceAuditReportPage(_page, _fixture.WebBaseUrl, routeSegment);
            await _page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 20_000 });
            Assert.False(await report.HasLoadErrorAsync(), $"'{cardTitle}' report page reported a load error");
            Assert.True(await report.AreFiltersVisibleAsync(), "Expected the filters card to be visible");
            Assert.True(await report.IsGridVisibleAsync(), "Expected the report grid to be visible");
        }
    }

    [Fact]
    public async Task NonHrPersona_Catalogue_DoesNotShowAdministrationCategory_OrGovernanceCards()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // A Recruiter still holds baseline reporting:view, so the catalogue itself renders (with the
        // Recruitment-category cards) — it just must not contain the Administration governance ones.
        await catalog.GoToAsync(AcmeId);

        Assert.False(
            await _page.Locator("h5").GetByText("Administration", new() { Exact = true }).IsVisibleAsync(),
            "Did not expect an 'Administration' category heading for a non-HR persona");

        foreach (var card in new[] { "User Activity", "Administrative Changes", "Security Events", "Compliance Status" })
        {
            Assert.False(await catalog.HasCardAsync(card),
                $"Did not expect the '{card}' governance card to be visible to a non-HR persona");
        }
    }

    [Theory]
    [MemberData(nameof(GovernanceCards))]
    public async Task NonHrPersona_DirectNavigationToGovernanceRoute_RedirectsToAccessDenied(string cardTitle, string routeSegment)
    {
        _ = cardTitle;
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/reporting/governance/{routeSegment}");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });
        await WaitForUrlToStopContainingAsync($"/reporting/governance/{routeSegment}");

        Assert.Contains("/access-denied", _page.Url);
    }
}
