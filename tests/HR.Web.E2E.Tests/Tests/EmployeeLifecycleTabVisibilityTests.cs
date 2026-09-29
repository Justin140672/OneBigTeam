using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class EmployeeLifecycleTabVisibilityTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static readonly Guid SarahChen = Guid.Parse("30000000-0000-0000-0000-000000000001");

    private static readonly Guid LauraId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    private const string LauraEmail = "laura.bennett@acme.example";

    private async Task StartLeavingProcessViaWizardAsync(
        StartLeavingProcessDialog dialog, string resignationDdMMyyyy, string reasonLabel)
    {
        await dialog.OpenAsync();
        await dialog.FillResignationReceivedDateAsync(resignationDdMMyyyy);
        await dialog.ClickNextAsync();

        var leavingDateRaw = await dialog.GetLeavingDateTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(leavingDateRaw),
            "Expected step 2 to auto-populate a proposed leaving date from the employee's effective notice period");

        await dialog.ClickNextAsync();

        await dialog.FillLastWorkingDayAsync(leavingDateRaw!);
        await dialog.ClickNextAsync();

        await dialog.SelectLeavingReasonAsync(reasonLabel);
        await dialog.ClickNextAsync();

        await dialog.ConfirmAsync();
        Assert.False(await dialog.IsVisibleAsync(),
            "Expected the Start Leaving Process dialog to close after a successful submission");

        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });
    }

    private async Task<Guid> CreateEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, string suffix)
    {
        _ = empList;
        var seeded = suffix == "Multi"
            ? SeededE2eEmployees.LifecycleTabVisibility[0]
            : SeededE2eEmployees.LifecycleTabVisibility[1];
        await empEdit.GoToAsync(AcmeId, seeded.EmployeeId);
        return seeded.EmployeeId;
    }

    [Fact]
    public async Task Employee_WithNoLifecycleProcesses_HidesAllThreeLifecycleTabs()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SarahChen);

        Assert.False(
            await EmployeeEditPage.IsSectionTabPresentAsync(_page, "Onboarding"),
            "Expected no 'Onboarding' tab for an employee who never had a plan");
        Assert.False(
            await EmployeeEditPage.IsSectionTabPresentAsync(_page, "Probation"),
            "Expected no 'Probation' tab for an employee who never had a record");
        Assert.False(
            await EmployeeEditPage.IsSectionTabPresentAsync(_page, "Offboarding"),
            "Expected no 'Offboarding' tab for an employee who never had a plan");

        Assert.False(
            await _page.GetByRole(AriaRole.Button, new() { Name = "Start Offboarding" }).IsVisibleAsync(),
            "Expected no manual 'Start Offboarding' entry point anywhere");
    }

    [Fact]
    public async Task Employee_WithMultipleActiveLifecycleProcesses_ShowsAllRelevantTabs()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList     = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit     = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var startDialog = new StartLeavingProcessDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, "Multi");

        Assert.True(
            await EmployeeEditPage.IsSectionTabPresentAsync(_page, "Onboarding"),
            "Expected the Onboarding tab to already be visible on a freshly created employee");
        Assert.False(
            await EmployeeEditPage.IsSectionTabPresentAsync(_page, "Offboarding"),
            "Expected no Offboarding tab yet");

        await StartLeavingProcessViaWizardAsync(startDialog, "01/09/2026", "Resignation");

        Assert.True(
            await EmployeeEditPage.IsSectionTabPresentAsync(_page, "Onboarding"),
            "Expected the Onboarding tab to remain visible");
        Assert.True(
            await EmployeeEditPage.IsSectionTabPresentAsync(_page, "Offboarding"),
            "Expected the Offboarding tab to now also be visible");
    }

    [Fact]
    public async Task OffboardingTab_RemainsVisibleAsHistoricalRecord_AfterCompletion()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList     = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit     = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var startDialog = new StartLeavingProcessDialog(_page);
        var inbox       = new HrInboxPage(_page, _fixture.WebBaseUrl);
        var profile     = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var taskView    = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var employeeId = await CreateEmployeeAsync(empList, empEdit, "OffComplete");

        await StartLeavingProcessViaWizardAsync(startDialog, "01/09/2026", "Resignation");

        Assert.True(await EmployeeEditPage.IsSectionTabPresentAsync(_page, "Offboarding"),
            "Expected the Offboarding tab to be visible once started");

        string[] taskFragments =
        [
            "Review outstanding documents for employee exit",
            "Conduct exit interview",
            "Revoke system access and accounts",
            "Arrange handover and knowledge transfer",
            "Notify IT and Finance of employee exit",
        ];

        foreach (var fragment in taskFragments)
        {
            await inbox.GoToAsync(AcmeId);
            var matchCount = (await inbox.GetTaskTitlesAsync())
                .Count(t => t.Contains(fragment, StringComparison.OrdinalIgnoreCase));

            for (var i = 0; i < matchCount; i++)
            {
                await inbox.GoToAsync(AcmeId);
                var titles = await inbox.GetTaskTitlesAsync();
                var claimedTitle = titles.First(t => t.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                await inbox.ClaimAsync(claimedTitle);

                await profile.GoToAsync(AcmeId, LauraId);
                await profile.OpenTasksTabAsync();

                var claimedRow = _page.Locator(".e-row")
                    .Filter(new() { HasText = claimedTitle })
                    .Filter(new() { HasNotText = "Completed" })
                    .First;

                await claimedRow.WaitForAsync(new() { Timeout = 15_000 });
                await claimedRow.Locator("button[title='View']").ClickAsync();

                await taskView.WaitForLoadedAsync();
                await taskView.CompleteGeneralTaskAsync();
                await taskView.CloseAsync();
            }
        }

        // Revisiting the employee's profile should still show the unified "Leaving & Offboarding"
        // tab — SPEC-OFF-01 deliberately keeps it visible forever once any leaving process has ever
        // existed for the employee ("Historical attempts must remain accessible and clearly
        // distinguished from the current attempt" — see GetEmployeeHandler's showLeavingTab, which
        // is `hasAnyLeavingProcess` with no completion/cancellation check at all). There is no
        // manual "Start Offboarding" entry point to reappear, though — that button no longer exists
        // anywhere in the UI regardless of tab visibility.
        await empEdit.GoToAsync(AcmeId, employeeId);

        await EmployeeEditPage.SelectOwningGroupAsync(_page, "Offboarding");
        await Assertions.Expect(EmployeeEditPage.SectionTab(_page, "Offboarding"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await EmployeeEditPage.SectionTab(_page, "Offboarding").ClickAsync();
        await Assertions.Expect(_page.GetByText("Completed", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        Assert.False(
            await _page.GetByRole(AriaRole.Button, new() { Name = "Start Offboarding" }).IsVisibleAsync(),
            "Expected no manual 'Start Offboarding' entry point anywhere");
    }
}
