using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

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

        await panel.ClickRefreshAsync();

        var status = await panel.StatusTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(status), "Expected a latest-export status to be shown");
        Assert.True(await panel.HistoryRowCountAsync() >= 1, "Expected at least one export in the history grid");
    }
}
