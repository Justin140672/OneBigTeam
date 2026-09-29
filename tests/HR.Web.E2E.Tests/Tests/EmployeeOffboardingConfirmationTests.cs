using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class EmployeeOffboardingConfirmationTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string LauraEmail = "laura.bennett@acme.example";

    private static readonly SemaphoreSlim _sharedEmployeeLock = new(1, 1);
    private static Guid? _sharedEmployeeId;

    private async Task<Guid> GetSharedEmployeeAsync(EmployeeListPage empList, EmployeeEditPage empEdit)
    {
        if (_sharedEmployeeId is { } cached)
        {
            await empEdit.GoToViewAsync(AcmeId, cached);
            return cached;
        }

        await _sharedEmployeeLock.WaitAsync();
        try
        {
            if (_sharedEmployeeId is { } cachedAfterLock)
            {
                await empEdit.GoToViewAsync(AcmeId, cachedAfterLock);
                return cachedAfterLock;
            }

            var created = await CreateEmployeeAsync(empList, empEdit, "Shared");
            _sharedEmployeeId = created;
            return created;
        }
        finally
        {
            _sharedEmployeeLock.Release();
        }
    }

    private async Task<Guid> CreateEmployeeAsync(EmployeeListPage empList, EmployeeEditPage empEdit, string suffix)
    {
        _ = empList;
        _ = suffix;
        var id = SeededE2eEmployees.OffboardingConfirmation[0].EmployeeId;
        await empEdit.GoToViewAsync(AcmeId, id);
        return id;
    }

    [Fact]
    public async Task StartOffboarding_IsReachable_ViaMoreActionsMenu_NotAsAHeaderButton()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await GetSharedEmployeeAsync(empList, empEdit);

        Assert.False(
            await _page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Start Leaving Process" }).IsVisibleAsync(),
            "The direct header 'Start Leaving Process' button should no longer exist");
        Assert.True(await empEdit.HasStartOffboardingMenuItemAsync(),
            "Expected 'Start offboarding' to be present in the 'More actions' overflow menu instead");
    }

    [Fact]
    public async Task StartOffboardingDialog_ShowsConsequencesExplanation_OnConfirmStep()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dialog = new StartLeavingProcessDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await GetSharedEmployeeAsync(empList, empEdit);

        await dialog.OpenAsync();
        await dialog.FillResignationReceivedDateAsync("01/09/2026");
        await dialog.ClickNextAsync();

        var leavingDateRaw = await dialog.GetLeavingDateTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(leavingDateRaw));
        await dialog.ClickNextAsync();

        await dialog.FillLastWorkingDayAsync(leavingDateRaw!);
        await dialog.ClickNextAsync();

        await dialog.SelectLeavingReasonAsync("Resignation");
        await dialog.ClickNextAsync();

        Assert.Equal("5. Confirm", await dialog.GetActiveStepLabelAsync());

        var confirmDialog = _page.GetByRole(Microsoft.Playwright.AriaRole.Dialog, new() { Name = "Start Leaving Process" });
        await Microsoft.Playwright.Assertions.Expect(confirmDialog)
            .ToContainTextAsync("This employee has resigned", new() { Timeout = 10_000 });
        await Microsoft.Playwright.Assertions.Expect(confirmDialog)
            .ToContainTextAsync("no separate \"start offboarding\" step", new() { Timeout = 5_000 });
        await Microsoft.Playwright.Assertions.Expect(confirmDialog)
            .ToContainTextAsync("offboarding checklist", new() { Timeout = 5_000 });
    }

    [Fact]
    public async Task CancellingStartOffboardingDialog_LeavesEmployeeActiveAndUnchanged()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dialog = new StartLeavingProcessDialog(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await GetSharedEmployeeAsync(empList, empEdit);

        var originalStatus = await empEdit.GetEmployeeStatusBadgeTextAsync();

        await dialog.OpenAsync();
        await dialog.FillResignationReceivedDateAsync("01/09/2026");
        await dialog.ClickNextAsync();

        await dialog.CancelAsync();
        Assert.False(await dialog.IsVisibleAsync(),
            "Expected the Start offboarding dialog to close after Cancel");

        Assert.Equal(originalStatus, await empEdit.GetEmployeeStatusBadgeTextAsync());
        Assert.True(await empEdit.HasStartOffboardingMenuItemAsync(),
            "'Start offboarding' should still be offered after a cancelled attempt");
    }
}
