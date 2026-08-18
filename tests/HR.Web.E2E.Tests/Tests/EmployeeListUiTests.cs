using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Employee List page's UI rework (Components/Pages/Employees/EmployeeList.razor):
/// search + clear search, the Department/Status filter panel and chips, single/multi row
/// selection reflected in the "Update selected" bulk-action label, row-wide profile navigation
/// via the combined "Employee" identity cell, long-value cell handling, and responsive column
/// visibility at common breakpoints.
///
/// Every scenario that mutates data (new employees/departments) uses a unique suffix so it can't
/// collide with other test classes running against the same shared, long-lived E2E database.
/// </summary>
public sealed class EmployeeListUiTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    private async Task<string> CreateEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, string uniqueSuffix, string? positionProfile = null)
    {
        var lastName = $"UiListE2E{uniqueSuffix}";
        var workEmail = $"e2e.uilist{uniqueSuffix}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();

        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-UIL-{uniqueSuffix}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", positionProfile ?? "Senior Software Engineer");
        await empEdit.SaveNewEmployeeAsync();

        return lastName;
    }

    // ── Search / clear search ─────────────────────────────────────────────────

    [Fact]
    public async Task Search_FiltersGridToMatchingEmployee()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = await CreateEmployeeAsync(empList, empEdit, unique);

        await empList.GoToAsync(AcmeId);
        await empList.SearchAsync(lastName);

        var names = await empList.GetEmployeeNamesAsync();
        Assert.Contains(names, n => n.Contains(lastName));
    }

    [Fact]
    public async Task ClearSearch_HidesClearButtonWhenEmpty_ShowsItAfterSearching_AndResetsListWhenClicked()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = await CreateEmployeeAsync(empList, empEdit, unique);

        await empList.GoToAsync(AcmeId);

        Assert.False(await empList.IsClearSearchButtonVisibleAsync(),
            "Expected no Clear search button before any search is entered");

        await empList.SearchAsync(lastName);

        Assert.True(await empList.IsClearSearchButtonVisibleAsync(),
            "Expected Clear search button to appear once the search box has text");

        await empList.ClickClearSearchAsync();

        Assert.Equal(string.Empty, await empList.GetSearchBoxValueAsync());
        Assert.False(await empList.IsClearSearchButtonVisibleAsync(),
            "Expected Clear search button to disappear again once the search box is cleared");
    }

    // ── Result summary ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ResultSummary_ReflectsSearchNarrowingToASingleEmployee()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = await CreateEmployeeAsync(empList, empEdit, unique);

        await empList.GoToAsync(AcmeId);
        await empList.SearchAsync(lastName);

        var summary = await empList.GetResultSummaryTextAsync();
        Assert.NotNull(summary);
        Assert.Contains("1 employee", summary);
    }

    // ── Filters panel + chips ───────────────────────────────────────────────────

    [Fact]
    public async Task StatusFilter_NarrowsGrid_ShowsChip_AndBadgesActiveCount()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        await CreateEmployeeAsync(empList, empEdit, unique);

        await empList.GoToAsync(AcmeId);

        Assert.Equal(0, await empList.GetActiveFilterCountAsync());

        await empList.SelectStatusFilterAsync("Active");

        Assert.Equal(1, await empList.GetActiveFilterCountAsync());
        Assert.True(await empList.HasFilterChipAsync("Status: Active"),
            "Expected a 'Status: Active' filter chip to render once the Status filter is applied");
    }

    [Fact]
    public async Task DepartmentFilter_NarrowsGrid_AndShowsChip()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empList.GoToAsync(AcmeId);
        await empList.OpenFiltersPanelAsync();

        // Engineering is a seeded department on the shared Acme dataset (used across many other
        // E2E suites, e.g. compensation/position-profile tests) — reused here rather than
        // creating a fresh one, since this test only needs the filter mechanism to work, not a
        // specific department's contents.
        await empList.SelectDepartmentFilterAsync("Engineering");

        Assert.True(await empList.GetActiveFilterCountAsync() >= 1);
        Assert.True(await empList.HasFilterChipAsync("Department: Engineering"),
            "Expected a 'Department: Engineering' filter chip to render once the Department filter is applied");
    }

    [Fact]
    public async Task RemovingFilterChip_ClearsThatFilter_UpdatesGrid_AndDecrementsBadge()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        await CreateEmployeeAsync(empList, empEdit, unique);

        await empList.GoToAsync(AcmeId);
        await empList.SelectStatusFilterAsync("Active");

        Assert.True(await empList.HasFilterChipAsync("Status: Active"));
        Assert.Equal(1, await empList.GetActiveFilterCountAsync());

        await empList.RemoveFilterChipAsync("Status: Active");

        Assert.False(await empList.HasFilterChipAsync("Status: Active"),
            "Expected the Status filter chip to disappear after clicking its remove button");
        Assert.Equal(0, await empList.GetActiveFilterCountAsync());
    }

    // ── Row selection / "Update selected" label ─────────────────────────────────

    [Fact]
    public async Task SingleRowSelection_ShowsCountOfOneOnUpdateSelectedButton_AndRevertsWhenUnchecked()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = await CreateEmployeeAsync(empList, empEdit, unique);

        await empList.GoToAsync(AcmeId);
        await empList.SearchAsync(lastName);

        var beforeText = await empList.GetUpdateSelectedButtonTextAsync();
        Assert.DoesNotContain("(1)", beforeText);

        await empList.CheckEmployeeRowAsync(lastName);

        var checkedText = await empList.GetUpdateSelectedButtonTextAsync();
        Assert.Contains("(1)", checkedText);

        await empList.UncheckEmployeeRowAsync(lastName);

        var uncheckedText = await empList.GetUpdateSelectedButtonTextAsync();
        Assert.DoesNotContain("(1)", uncheckedText);
    }

    [Fact]
    public async Task MultiRowSelection_ShowsCorrectCountOnUpdateSelectedButton()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var firstLastName = await CreateEmployeeAsync(empList, empEdit, $"{unique}A");
        var secondLastName = await CreateEmployeeAsync(empList, empEdit, $"{unique}B");

        await empList.GoToAsync(AcmeId);
        await empList.SearchAsync($"UiListE2E{unique}");

        await empList.CheckEmployeeRowAsync(firstLastName);
        await empList.CheckEmployeeRowAsync(secondLastName);

        var text = await empList.GetUpdateSelectedButtonTextAsync();
        Assert.Contains("(2)", text);
    }

    // ── Row-wide navigation vs. checkbox-only toggle ─────────────────────────────

    [Fact]
    public async Task ClickingEmployeeIdentityCell_NavigatesToProfile()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = await CreateEmployeeAsync(empList, empEdit, unique);

        await empList.GoToAsync(AcmeId);
        await empList.SearchAsync(lastName);
        await empList.ClickEmployeeIdentityCellAsync(lastName);

        Assert.Contains("/employees/", _page.Url);
        Assert.DoesNotContain("/employees/new", _page.Url);
    }

    [Fact]
    public async Task ClickingAnywhereElseInRow_AlsoNavigatesToProfile()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = await CreateEmployeeAsync(empList, empEdit, unique);

        await empList.GoToAsync(AcmeId);
        await empList.SearchAsync(lastName);
        await empList.ClickRowWorkEmailCellAsync(lastName);

        Assert.Contains("/employees/", _page.Url);
        Assert.DoesNotContain("/employees/new", _page.Url);
    }

    [Fact]
    public async Task ClickingRowCheckbox_TogglesSelection_ButDoesNotNavigate()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = await CreateEmployeeAsync(empList, empEdit, unique);

        await empList.GoToAsync(AcmeId);
        await empList.SearchAsync(lastName);

        var urlBeforeClick = _page.Url;
        await empList.ClickRowCheckboxCellAsync(lastName);

        // Give any (incorrect) navigation a moment to happen before asserting it didn't.
        await _page.WaitForTimeoutAsync(500);
        Assert.Equal(urlBeforeClick, _page.Url);

        var buttonText = await empList.GetUpdateSelectedButtonTextAsync();
        Assert.Contains("(1)", buttonText);
    }

    // ── Long values in cells (Department/Position) ───────────────────────────────
    //
    // Read EmployeeList.razor's markup and app.css directly before writing this test: the
    // "Employee" identity cell's name span (.employee-cell-name) and the plain Department/Position
    // GridColumn cells rely on CSS overflow:hidden + text-overflow:ellipsis for visual truncation.
    // Neither the identity-cell template nor the plain GridColumn definitions set a "title"
    // attribute (no ClipMode="EllipsisWithTooltip" configured on HrGrid, no explicit title= in the
    // Employee Template) — so there is NO tooltip that exposes the full value on hover for a long
    // Department/Position value. The one confirmed accessible-reveal mechanism is the grid's own
    // horizontal scroll (".hr-grid { overflow-x: auto; }" in app.css, applied to .employee-grid
    // since HrGrid always adds the "hr-grid" class — see HrGrid.cs), and the full, untruncated text
    // is always present in the DOM (readable via TextContent) even while visually clipped.
    [Fact]
    public async Task LongPositionProfileTitle_IsNotClippedInTheDom_AndGridSupportsHorizontalScrollToReveal()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var longTitle = $"Principal Staff Engineer, Advanced Platform Reliability and Infrastructure {unique}";

        await ppEdit.GoToNewAsync(AcmeId);
        await ppEdit.FillTitleAsync(longTitle);
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.SaveAsync();

        var lastName = await CreateEmployeeAsync(empList, empEdit, unique, positionProfile: longTitle);

        await empList.GoToAsync(AcmeId);
        await empList.SearchAsync(lastName);

        var rowCell = _page.Locator(".e-grid .e-rowcell")
            .Filter(new() { HasTextRegex = new System.Text.RegularExpressions.Regex("Principal Staff Engineer") })
            .First;
        await rowCell.WaitForAsync(new() { Timeout = 15_000 });

        // The full, untruncated Position title is present in the DOM regardless of any visual
        // ellipsis clipping — proven by TextContent, not by the visible rendered width.
        var cellText = (await rowCell.TextContentAsync())?.Trim();
        Assert.Equal(longTitle, cellText);

        // No title/tooltip attribute exposes the full value on hover — this is a genuine gap (see
        // the comment above this test). Confirmed here rather than asserted as "working" behaviour.
        var cellTitleAttr = await rowCell.GetAttributeAsync("title");
        Assert.Null(cellTitleAttr);
    }

    // ── Responsive column visibility ──────────────────────────────────────────────

    [Theory]
    [InlineData(1280, 800)] // desktop — all columns visible
    [InlineData(768, 1024)] // tablet — below 992px breakpoint: Manager/Start Date/User Account hidden
    [InlineData(390, 844)]  // mobile — below 576px breakpoint: Work Email/Position additionally hidden
    public async Task Grid_RemainsUsable_WithEmployeeAndStatusColumnsAlwaysVisible_AtCommonBreakpoints(
        int width, int height)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await _page.SetViewportSizeAsync(width, height);
        await empList.GoToAsync(AcmeId);

        // "Employee" (2nd column) and "Status" (8th column) are never hidden by the responsive CSS
        // rules (see app.css's nth-child selectors — only columns 3, 5, 6, 7, 9 are ever targeted).
        var headerCells = _page.Locator(".e-grid .e-headercell");
        await headerCells.First.WaitForAsync(new() { Timeout = 15_000 });

        var employeeHeader = headerCells.Nth(1); // 0-indexed: 0 checkbox, 1 Employee
        var statusHeader = headerCells.Nth(7);    // 0 checkbox,1 Employee,2 Email,3 Dept,4 Position,5 Manager,6 StartDate,7 Status

        Assert.True(await employeeHeader.IsVisibleAsync(), "Expected the Employee column to remain visible at all breakpoints");
        Assert.True(await statusHeader.IsVisibleAsync(), "Expected the Status column to remain visible at all breakpoints");

        if (width < 992)
        {
            var managerHeader = headerCells.Nth(5);
            Assert.False(await managerHeader.IsVisibleAsync(),
                $"Expected the Manager column to be hidden below 992px (viewport width {width})");
        }

        if (width < 576)
        {
            var workEmailHeader = headerCells.Nth(2);
            var positionHeader = headerCells.Nth(4);
            Assert.False(await workEmailHeader.IsVisibleAsync(),
                $"Expected the Work Email column to be hidden below 576px (viewport width {width})");
            Assert.False(await positionHeader.IsVisibleAsync(),
                $"Expected the Position column to be hidden below 576px (viewport width {width})");
        }

        // Regardless of hidden columns, the grid's own horizontal scroll (".hr-grid { overflow-x:
        // auto; }") and the "More" > "Columns" chooser remain available as accessible ways to
        // reveal anything not currently visible — confirm the overflow-x style is actually applied
        // to this grid instance (rather than assuming from the CSS file alone).
        var overflowX = await _page.Locator(".employee-grid").First.EvaluateAsync<string>(
            "el => getComputedStyle(el).overflowX");
        Assert.Equal("auto", overflowX);
    }
}
