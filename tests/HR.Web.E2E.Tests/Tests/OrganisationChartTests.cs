using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Organisation Chart page: HR sees the nav link and the rendered chart contains a
/// seeded employee's card (Name/Job Title/Department), a plain Employee is redirected away,
/// clicking a card opens that employee's profile, and the Employee page's "View Org Chart"
/// button opens the chart centred on and highlighting that employee.
/// </summary>
[Collection("E2E")]
public sealed class OrganisationChartTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Laura Bennett — seeded HR Manager, People & HR department (EmployeesModule.SeedEmployeesAsync).
    private static readonly Guid LauraId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string TomEmail = "tom.williams@acme.example";

    [Fact]
    public async Task HrAdministrator_SeesOrganisationChartNavLink()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // "Organisation Chart" lives inside the "People" submenu (MainLayout.razor). Confirmed by
        // actually running this: ".app-nav-menu"'s DOM content is only the five top-level labels
        // ("DashboardPeopleAssetsRecruitmentLeave") until "People" is clicked — Syncfusion's
        // SfMenu doesn't just CSS-hide the submenu, it isn't rendered at all until expanded, and
        // (per Syncfusion's usual popup-based submenu rendering) it may render as a portal
        // elsewhere in the DOM rather than nested inside ".app-nav-menu" — so search the whole
        // page for the resulting text instead of assuming it stays inside that container.
        await _page.Locator(".app-nav-menu").GetByText("People", new() { Exact = true }).ClickAsync();

        var orgChartLink = _page.GetByText("Organisation Chart", new() { Exact = true });
        await orgChartLink.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        Assert.True(await orgChartLink.IsVisibleAsync(),
            "Expected an 'Organisation Chart' nav item to be visible to an HR Administrator after expanding People");
    }

    [Fact]
    public async Task HrAdministrator_OrganisationChart_RendersSeededEmployeeCard()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/organisation-chart");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        // Sarah Chen (CTO, Engineering) is seeded as Active with no manager — the root of the
        // chart — so her card should render once the diagram has laid itself out.
        var sarahCard = _page.Locator(".org-chart-card").Filter(new() { HasText = "Sarah Chen" });
        await sarahCard.First.WaitForAsync(new() { Timeout = 15_000 });

        Assert.True(await sarahCard.First.IsVisibleAsync(),
            "Expected Sarah Chen's card to be visible on the organisation chart");

        var cardText = await sarahCard.First.InnerTextAsync();
        Assert.Contains("Chief Technology Officer", cardText);
        Assert.Contains("Engineering", cardText);
    }

    [Fact]
    public async Task HrAdministrator_ClickingACard_OpensThatEmployeesProfile()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/organisation-chart");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var sarahCard = _page.Locator(".org-chart-card").Filter(new() { HasText = "Sarah Chen" });
        await sarahCard.First.WaitForAsync(new() { Timeout = 15_000 });
        await sarahCard.First.ClickAsync();

        await _page.WaitForURLAsync(
            new Regex(@"/employees/[0-9a-fA-F-]{36}(/|$|\?)"), new() { Timeout = 15_000 });

        Assert.DoesNotContain("/organisation-chart", _page.Url);
        Assert.Contains("/employees/", _page.Url);
    }

    [Fact]
    public async Task ViewOrgChartButton_OnEmployeeProfile_OpensChartWithThatEmployeeHighlighted()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees/{LauraId}");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        await _page.GetByRole(AriaRole.Link, new() { Name = "View Org Chart" }).ClickAsync();
        await _page.WaitForURLAsync(
            new Regex(@"/organisation-chart\?employeeId="), new() { Timeout = 15_000 });

        Assert.Contains($"employeeId={LauraId}", _page.Url, StringComparison.OrdinalIgnoreCase);

        var lauraCard = _page.Locator(".org-chart-card").Filter(new() { HasText = "Laura Bennett" });
        await lauraCard.First.WaitForAsync(new() { Timeout = 15_000 });

        var cardClass = await lauraCard.First.GetAttributeAsync("class");
        Assert.Contains("org-chart-card-highlighted", cardClass ?? string.Empty);
    }

    [Fact]
    public async Task OrganisationChart_ZoomControls_ChangeAndPersistZoomLevel()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/organisation-chart");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var zoomLevel = _page.Locator(".org-chart-zoom-level");
        await zoomLevel.WaitForAsync(new() { Timeout = 15_000 });

        var initialText = await zoomLevel.InnerTextAsync();

        await _page.GetByTitle("Zoom in").ClickAsync();
        await Assertions.Expect(zoomLevel).Not.ToHaveTextAsync(initialText, new() { Timeout = 10_000 });

        var zoomedText = await zoomLevel.InnerTextAsync();

        // Reloading re-establishes a brand-new Blazor circuit, so the zoom level surviving the
        // reload can only be explained by the localStorage-backed restore in OnAfterRenderAsync,
        // not any in-memory state.
        await _page.ReloadAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var zoomLevelAfterReload = _page.Locator(".org-chart-zoom-level");
        await zoomLevelAfterReload.WaitForAsync(new() { Timeout = 15_000 });

        Assert.Equal(zoomedText, await zoomLevelAfterReload.InnerTextAsync());
    }

    [Fact]
    public async Task PlainEmployee_IsRedirectedAway_FromOrganisationChartPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/organisation-chart");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/organisation-chart"),
            $"Expected a plain employee to be redirected away from the organisation chart page, but ended up at: {finalUrl}");
    }
}
