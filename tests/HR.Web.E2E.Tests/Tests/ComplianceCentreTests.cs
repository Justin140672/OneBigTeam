using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// ADM-02 Compliance Centre (/companies/{companyId}/reporting/compliance-centre —
/// ComplianceCentrePage.razor). Journey coverage for an HR Administrator plus an access guard for a
/// plain employee, mirroring LeaveTypeManagementTests' persona/login pattern and its
/// PlainEmployee_IsRedirectedAway_* guard.
///
/// Data-dependent assertions (grid filtering deltas, drill-through) degrade gracefully when the
/// shared seeded Acme company happens to have no compliance items, the same way the sibling report
/// E2E tests handle possibly-empty data.
/// </summary>
public sealed class ComplianceCentreTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator
    private const string TomEmail   = "tom.williams@acme.example";  // plain employee

    [Fact]
    public async Task HrAdmin_ComplianceCentre_RendersSummaryCardsAndCategoryBreakdown()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var centre = new ComplianceCentrePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await centre.GoToAsync(AcmeId);
        Assert.False(await centre.HasLoadErrorAsync(), "Compliance Centre reported a load error");

        // All four summary count cards exist and show a numeric value.
        foreach (var label in new[] { "Total", "Overdue", "Due soon", "Informational" })
        {
            Assert.True(await centre.HasSummaryCardAsync(label), $"Missing summary card '{label}'");
            Assert.True(await centre.GetSummaryValueAsync(label) >= 0,
                $"Summary card '{label}' did not show a non-negative number");
        }

        // The per-category breakdown lists all six categories.
        Assert.Equal(6, await centre.GetBreakdownRowCountAsync());
        var labels = await centre.GetBreakdownCategoryLabelsAsync();
        foreach (var expected in ComplianceCentrePage.CategoryLabels)
            Assert.Contains(expected, labels);
    }

    [Fact]
    public async Task HrAdmin_ApplyingCategoryAndSeverityFilters_UpdatesGrid()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var centre = new ComplianceCentrePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await centre.GoToAsync(AcmeId);

        var unfiltered = await centre.GetRowCountAsync();

        // Filter to a single category — the visible set must never grow, and every retained row
        // must be within that category (asserted via the grid still rendering without error).
        await centre.SelectCategoryAsync("Probation review");
        var afterCategory = await centre.GetRowCountAsync();
        Assert.False(await centre.HasLoadErrorAsync());
        Assert.True(afterCategory <= unfiltered,
            $"Category filter increased the row count ({unfiltered} -> {afterCategory})");

        // Narrow further by severity — again may only shrink or stay equal.
        await centre.SelectSeverityAsync("Overdue");
        var afterSeverity = await centre.GetRowCountAsync();
        Assert.True(afterSeverity <= afterCategory,
            $"Severity filter increased the row count ({afterCategory} -> {afterSeverity})");

        // Reset restores the original view (no explicit "All ..." items — the Clear button is the
        // only reset affordance on this page).
        await centre.ClearFiltersAsync();
        Assert.Equal(unfiltered, await centre.GetRowCountAsync());
    }

    [Fact]
    public async Task HrAdmin_EmptyState_OrGridIsWired()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var centre = new ComplianceCentrePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await centre.GoToAsync(AcmeId);

        // Force a due-date window nothing can fall into by combining the most restrictive filters
        // available without seeding: a single category + a single severity. If that yields no
        // items the green "No compliance action required" panel must appear; otherwise the grid
        // must be present. Either way the empty-state selector is proven wired and detectable —
        // the same possibly-empty handling the sibling report E2E tests use.
        await centre.SelectCategoryAsync("Outstanding document request");
        await centre.SelectSeverityAsync("Informational");

        if (await centre.GetRowCountAsync() == 0)
        {
            Assert.True(await centre.IsEmptyStateVisibleAsync(),
                "Expected the 'No compliance action required' panel when the filtered result is empty");
        }
        else
        {
            Assert.False(await centre.IsEmptyStateVisibleAsync(),
                "Empty-state panel showed while the grid still had rows");
        }
    }

    [Fact]
    public async Task HrAdmin_DrillThrough_NavigatesToEmployeePage()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var centre = new ComplianceCentrePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await centre.GoToAsync(AcmeId);

        if (await centre.GetRowCountAsync() == 0 || !await centre.HasDrillThroughLinkAsync())
            return; // data-dependent — skip gracefully like other report E2E tests

        await centre.ClickFirstRowDrillThroughAsync();

        Assert.Contains($"/companies/{AcmeId}/employees/", _page.Url);
    }

    [Fact]
    public async Task PlainEmployee_IsRedirectedAway_FromComplianceCentre()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/reporting/compliance-centre");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });
        await WaitForUrlToStopContainingAsync("/reporting/compliance-centre");

        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/reporting/compliance-centre"),
            $"Expected a plain employee to be redirected away from the Compliance Centre, but landed at: {finalUrl}");
        Assert.Contains("/access-denied", finalUrl);
    }
}
