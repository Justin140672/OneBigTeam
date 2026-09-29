using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class EmployeeProbationTabTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId       = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid CarlosRivera = Guid.Parse("30000000-0000-0000-0000-000000000010");

    private static readonly Guid JamesOkafor  = Guid.Parse("30000000-0000-0000-0000-000000000002");

    private static readonly Guid SarahChen    = Guid.Parse("30000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task ProbationTab_IsVisible_On_Employee_Edit_Page()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, CarlosRivera);

        await EmployeeEditPage.SelectOwningGroupAsync(_page, "Probation");
        await Assertions.Expect(EmployeeEditPage.SectionTab(_page, "Probation"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task ProbationTab_ShowsProbationPeriodSummaryPanel()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, CarlosRivera);
        await empEdit.OpenProbationTabAsync();

        Assert.True(await empEdit.HasProbationPeriodSummaryPanelAsync(),
            "Expected the probation period summary panel (progress bar) to be visible");
    }

    [Fact]
    public async Task ProbationTab_ShowsReviewHistoryGrid()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, CarlosRivera);
        await empEdit.OpenProbationTabAsync();

        Assert.True(await empEdit.HasProbationReviewsGridAsync(),
            "Expected the Syncfusion review history grid to be visible on the Probation tab");
    }

    [Fact]
    public async Task ProbationTab_ShowsActiveOrReviewDueStatus()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, CarlosRivera);
        await empEdit.OpenProbationTabAsync();

        var status = await empEdit.GetProbationStatusBadgeTextAsync();
        Assert.True(
            status is "Active" or "Review Due" or "Extended",
            $"Expected an in-progress probation status, got '{status}'");
    }

    [Fact]
    public async Task ProbationTab_IsHidden_ForEmployeeWithOnlyAPassedRecord()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, JamesOkafor);

        Assert.False(
            await EmployeeEditPage.IsSectionTabPresentAsync(_page, "Probation"),
            "Expected no 'Probation' tab for an employee whose only probation record is Passed");
    }

    [Fact]
    public async Task ProbationTab_IsHidden_ForEmployeeWhoNeverHadARecord()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SarahChen);

        Assert.False(
            await EmployeeEditPage.IsSectionTabPresentAsync(_page, "Probation"),
            "Expected no 'Probation' tab for an employee who never had a probation record");
    }
}
