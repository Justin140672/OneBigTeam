using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Organisation Chart page: HR sees the nav link and the rendered chart contains a
/// seeded employee's card (Name/Job Title/Department), a plain Employee is redirected away,
/// clicking a card opens that employee's profile, and the Employee page's "More actions" &gt;
/// "View Organisation Chart" menu item opens the chart centred on and highlighting that employee.
/// </summary>
public sealed class OrganisationChartTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
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

        // "Organisation Chart" lives inside the "People and users" submenu group (MainLayout.razor
        // / AdminNavigation.Sections). Syncfusion's SfMenu doesn't render a group's children at all
        // until it is expanded, and then as a popup (role="menuitem" nodes) that may be portaled
        // outside ".app-nav-menu" — so click the group, then search the whole page for the child.
        await _page.Locator(".app-nav-menu").GetByText("People and users", new() { Exact = true }).ClickAsync();

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

        // "View Org Chart" moved into the "More actions" overflow menu and was renamed
        // "View Organisation Chart" — see EmployeeEdit.razor's BuildMoreActionsItems.
        await _page.GetByRole(AriaRole.Button, new() { Name = "More actions" }).ClickAsync();
        await _page.GetByRole(AriaRole.Menuitem, new() { Name = "View Organisation Chart" }).ClickAsync();
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
    public async Task OrganisationChart_Does_Not_Have_Export_Button()
    {
        // The Export/Download toolbar button was removed — it never actually worked (see
        // backlog item on removing dead Org Chart Download button).
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/organisation-chart");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        await _page.Locator(".org-chart-zoom-toolbar").WaitForAsync(new() { Timeout = 15_000 });

        Assert.False(await _page.GetByTitle("Export as image").IsVisibleAsync(),
            "Expected no Export button on the organisation chart toolbar");
    }

    [Fact]
    public async Task OrganisationChart_DepartmentFilter_HasAllDepartmentsOption_AndNoEmploymentStatusFilter()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/organisation-chart");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var departmentGroup = _page.Locator(".col-md-3").Filter(new() { HasText = "Department" });
        await departmentGroup.Locator("span[role='combobox']").First.ClickAsync();
        await _page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });

        var allDepartmentsItem = _page.Locator(".e-popup.e-ddl .e-list-item").Filter(new() { HasText = "All Departments" });
        Assert.True(await allDepartmentsItem.First.IsVisibleAsync(),
            "Expected an explicit 'All Departments' item in the Department filter's dropdown list");

        await _page.Keyboard.PressAsync("Escape");

        Assert.False(await _page.GetByText("Employment Status").IsVisibleAsync(),
            "Expected the Employment Status filter to have been removed — the chart always shows Active employees only");
    }

    /// <summary>
    /// Typing a matching employee name into the chart's Search box (OrganisationChart.razor's
    /// OnSearchChanged) must highlight that employee's card via the same _focusEmployeeId mechanism
    /// the "View Organisation Chart" deep link uses — and must NOT surface the "no match" message.
    /// </summary>
    [Fact]
    public async Task Search_ByName_HighlightsMatchingCard_AndShowsNoNotFoundMessage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/organisation-chart");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });
        await _page.Locator(".org-chart-card").First.WaitForAsync(new() { Timeout = 15_000 });

        var search = _page.GetByPlaceholder("Search by name or employee number");
        await search.FillAsync("Laura Bennett");
        // HrTextBox (SfTextBox) only raises ValueChanged on blur/change, not on "input".
        await search.PressAsync("Enter");

        var lauraCard = _page.Locator(".org-chart-card").Filter(new() { HasText = "Laura Bennett" });
        await Assertions.Expect(lauraCard.First).ToHaveClassAsync(
            new Regex("org-chart-card-highlighted"), new() { Timeout = 10_000 });

        Assert.False(await _page.GetByText("No employee found matching").IsVisibleAsync(),
            "Expected no 'no match' message when the search term matches a seeded employee");
    }

    /// <summary>
    /// A search term that matches no employee must surface OrganisationChart.razor's
    /// _searchNotFound message and leave no card highlighted — the empty-result state.
    /// </summary>
    [Fact]
    public async Task Search_WithNoMatch_ShowsNotFoundMessage_AndHighlightsNothing()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/organisation-chart");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });
        await _page.Locator(".org-chart-card").First.WaitForAsync(new() { Timeout = 15_000 });

        var search = _page.GetByPlaceholder("Search by name or employee number");
        await search.FillAsync("Zzz No Such Person 9999");
        await search.PressAsync("Enter");

        await Assertions.Expect(_page.GetByText("No employee found matching"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        Assert.Equal(0, await _page.Locator(".org-chart-card-highlighted").CountAsync());
    }

    /// <summary>
    /// Clearing the search box after a no-match must clear the _searchNotFound message
    /// (OnSearchChanged's whitespace/empty branch) — no stale error text left behind.
    /// </summary>
    [Fact]
    public async Task Search_Cleared_RemovesNotFoundMessage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/organisation-chart");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });
        await _page.Locator(".org-chart-card").First.WaitForAsync(new() { Timeout = 15_000 });

        var search = _page.GetByPlaceholder("Search by name or employee number");
        await search.FillAsync("Zzz No Such Person 9999");
        await search.PressAsync("Enter");
        await Assertions.Expect(_page.GetByText("No employee found matching"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await search.FillAsync(string.Empty);
        await search.PressAsync("Enter");

        await Assertions.Expect(_page.GetByText("No employee found matching"))
            .ToBeHiddenAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task PlainEmployee_IsRedirectedAway_FromOrganisationChartPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/organisation-chart");
        // See WaitForUrlToStopContainingAsync's doc comment: the redirect is a client-side Blazor
        // NavigateTo, not a full navigation, so NetworkIdle is not a reliable completion signal.
        await WaitForUrlToStopContainingAsync("/organisation-chart");

        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/organisation-chart"),
            $"Expected a plain employee to be redirected away from the organisation chart page, but ended up at: {finalUrl}");
    }
}
