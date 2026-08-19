using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "Start offboarding" entry point on the employee profile page (moved into the
/// "More actions" overflow menu and renamed from "Start Leaving Process" — see
/// EmployeeEdit.razor's BuildMoreActionsItems/HandleMoreActionSelected) and the added
/// consequences-explanation paragraph on the StartLeavingProcessDialog's confirmation step. Does
/// not re-cover the full wizard end to end — see EmployeeLeavingProcessTests.cs for that; this
/// file focuses on the new confirmation-step text and the cancel-leaves-employee-unchanged path.
///
/// None of the three tests below ever actually confirms/completes the Start Leaving Process
/// wizard (the second only drives it as far as the "5. Confirm" step to check its text, the third
/// explicitly cancels), so no test here permanently mutates the employee — safe to share ONE
/// employee across all three instead of each paying the full New Employee form. Same
/// create-once-lazily pattern as EmployeeProfileViewEditModeTests.cs; see that file's own remarks
/// on why sharing is safe (xUnit runs methods within one class sequentially).
/// </summary>
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
        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"Offboard{suffix}{unique}";
        var workEmail = $"e2e.offboardconfirm.{suffix.ToLowerInvariant()}{unique}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();

        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "Senior Software Engineer");

        await empEdit.SaveNewEmployeeAsync();
        await empList.ClickEmployeeAsync(lastName);

        var match = System.Text.RegularExpressions.Regex.Match(
            _page.Url, @"/employees/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        return Guid.Parse(match.Groups[1].Value);
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

        var dialogText = await _page.GetByRole(Microsoft.Playwright.AriaRole.Dialog, new() { Name = "Start Leaving Process" }).TextContentAsync();
        Assert.Contains("Starting offboarding will begin this employee's leaving process", dialogText);
        Assert.Contains("offboarding checklist", dialogText, StringComparison.OrdinalIgnoreCase);
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

        // A freshly-created employee starts as "Draft" (Employee.Create's unconditional default —
        // there's no status field on the New Employee form itself), not "Active". The point of
        // this test is that cancelling leaves the status unchanged, not that it's specifically
        // "Active", so capture whatever the real baseline is rather than assuming one.
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
