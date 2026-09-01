using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Story 2: customer-facing organisation data export panel on the Subscription page
/// (OrganisationDataExportPanel.razor). Smoke coverage for a Company Administrator — the panel
/// loads, "Request export" creates a request and then reflects an active/disabled state, and the
/// history grid (when present) lists the request. Data-dependent assertions degrade gracefully
/// when the shared seeded company already has an in-flight export, mirroring the sibling report
/// E2E tests.
/// </summary>
public sealed class OrganisationDataExportPanelTests(PriyaShahPersonaFixture fixture)
    : RoleE2ETestBase<PriyaShahPersonaFixture>(fixture)
{
    private const string CompanyAdminEmail = "priya.shah@acme.example";

    [Fact]
    public async Task CompanyAdmin_CanRequestAnExport_AndPanelReflectsIt()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var panel = new OrganisationDataExportPanelPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await panel.GoToAsync();
        Assert.True(await panel.IsVisibleAsync(), "Organisation data export panel did not render");

        if (!await panel.IsRequestDisabledAsync())
        {
            await panel.ClickRequestAsync();
        }

        // After a request the export is Pending/InProgress or already Completed from a prior run —
        // either way the panel now shows a status and the history grid lists at least one row.
        await panel.ClickRefreshAsync();

        var status = await panel.StatusTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(status), "Expected a latest-export status to be shown");
        Assert.True(await panel.HistoryRowCountAsync() >= 1, "Expected at least one export in the history grid");
    }
}
