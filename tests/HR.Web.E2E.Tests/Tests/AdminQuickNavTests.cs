using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// The top-bar HR-only Employee Search palette (Ctrl+K), which replaced the old admin quick-nav:
/// - An HR admin (Laura Bennett) can search seeded Acme employees by name, employee number or work
///   email, and selecting a result lands on that employee's admin record (EmployeeEdit.razor, not
///   the self-service "/profile" route, which only ever shows the signed-in user's own record).
/// - A leaver is hidden by default and only shown once "Include leavers / archived employees" is ticked.
/// - The trigger is absent for every non-HR role, and Ctrl+K is inert for them.
/// - Esc closes the palette and returns focus to the trigger.
/// </summary>
public sealed class AdminQuickNavTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    // Seeded Acme employee used purely as a stable search target (never mutated):
    // "Sophie Laurent", ACME-007, sophie.laurent@acme.example — see EmployeesModule.SeedEmployeesAsync.
    private const string TargetFullName = "Sophie Laurent";
    private const string TargetEmployeeNumber = "ACME-007";
    private const string TargetWorkEmail = "sophie.laurent@acme.example";

    private async Task LoginAndOpenPaletteAsync(AdminQuickNavComponent palette, string email = LauraEmail)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(email);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees");
        await palette.OpenAsync();
    }

    [Fact]
    public async Task HrAdmin_SearchesByName_SeesEmployeeRow_AndSelectingLandsOnAdminRecord()
    {
        var palette = new AdminQuickNavComponent(_page);
        await LoginAndOpenPaletteAsync(palette);

        await palette.SearchAsync("Laurent");

        Assert.True(await palette.HasResultAsync(TargetFullName),
            $"Expected a result row for '{TargetFullName}' when searching by surname");
        Assert.True(await palette.HasResultAsync(TargetEmployeeNumber),
            "Expected the employee's number to be shown on the result row");

        await palette.ClickResultAsync(TargetFullName);

        // Lands on the admin employee record (EmployeeEdit.razor: "/companies/{c}/employees/{id}",
        // which may then swap to its "…/view" variant), never the self-service "/profile" route.
        await _page.WaitForURLAsync(
            new Regex(@$"/companies/{AcmeId}/employees/[0-9a-fA-F-]{{36}}(/view)?(\?|#|$)"),
            new() { Timeout = 30_000 });
        Assert.DoesNotContain("/profile", _page.Url);
    }

    [Fact]
    public async Task HrAdmin_SearchesByEmployeeNumber_ReturnsEmployee()
    {
        var palette = new AdminQuickNavComponent(_page);
        await LoginAndOpenPaletteAsync(palette);

        await palette.SearchAsync(TargetEmployeeNumber);

        Assert.True(await palette.HasResultAsync(TargetFullName),
            $"Expected '{TargetFullName}' when searching by employee number '{TargetEmployeeNumber}'");
    }

    [Fact]
    public async Task HrAdmin_SearchesByWorkEmail_ReturnsEmployee()
    {
        var palette = new AdminQuickNavComponent(_page);
        await LoginAndOpenPaletteAsync(palette);

        await palette.SearchAsync(TargetWorkEmail);

        Assert.True(await palette.HasResultAsync(TargetFullName),
            $"Expected '{TargetFullName}' when searching by work email '{TargetWorkEmail}'");
    }

    [Fact]
    public async Task HrAdmin_Leaver_IsHiddenByDefault_ButShownWhenIncludeLeaversTicked()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var dialog = new StartLeavingProcessDialog(_page);
        var palette = new AdminQuickNavComponent(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Turn a dedicated pool employee (unused by EmployeeLeavingProcessTests, which only takes
        // slots 0-6) into a leaver by driving the real Start Leaving Process wizard — there is no
        // seed data or API shortcut for this, mirroring EmployeeLeavingProcessTests.
        var leaver = SeededE2eEmployees.LeavingProcess[7];
        await empEdit.GoToAsync(AcmeId, leaver.EmployeeId);
        await MakeLeaverViaWizardAsync(dialog);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees");
        await palette.OpenAsync();

        await palette.SearchAsync(leaver.LastName);
        await palette.AssertNoResultAsync(leaver.FullName);

        await palette.SetIncludeLeaversAsync(true);

        Assert.True(await palette.HasResultAsync(leaver.FullName),
            "Expected the leaver to appear once 'Include leavers / archived employees' is ticked");
    }

    [Theory]
    [InlineData("tom.williams@acme.example")]   // plain Employee
    [InlineData("james.okafor@acme.example")]   // Employee Manager
    [InlineData("marcus.diallo@acme.example")]  // Recruiter
    [InlineData("priya.shah@acme.example")]     // Company-Administrator only
    public async Task NonHrRole_HasNoTrigger_AndCtrlKIsInert(string email)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var palette = new AdminQuickNavComponent(_page);

        await login.GoToAsync();
        await login.LoginAsync(email);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}");

        Assert.Equal(0, await palette.Trigger.CountAsync());

        await palette.OpenWithKeyboardAsync();
        await _page.WaitForTimeoutAsync(1_000);

        Assert.Equal(0, await palette.Dialog.CountAsync());
    }

    [Fact]
    public async Task Escape_ClosesPalette_AndReturnsFocusToTrigger()
    {
        var palette = new AdminQuickNavComponent(_page);
        await LoginAndOpenPaletteAsync(palette);

        await palette.SearchAsync("Lau");
        await palette.WaitForResultsSettledAsync();

        await palette.PressEscapeAsync();

        await palette.Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        await Assertions.Expect(palette.Trigger).ToBeFocusedAsync();
    }

    /// <summary>
    /// Drives the (opened-from-the-employee-edit-page) Start Leaving Process wizard end to end so
    /// the employee's status becomes "Leaving" (excluded from the directory search by default).
    /// Same flow as EmployeeLeavingProcessTests.StartLeavingProcessViaWizardAsync, trimmed to the
    /// steps needed here.
    /// </summary>
    private async Task MakeLeaverViaWizardAsync(StartLeavingProcessDialog dialog)
    {
        await dialog.OpenAsync();
        await dialog.FillResignationReceivedDateAsync("01/09/2026");
        await dialog.ClickNextAsync();

        var leavingDateRaw = await dialog.GetLeavingDateTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(leavingDateRaw),
            "Expected step 2 to auto-populate a proposed leaving date");
        await dialog.ClickNextAsync();

        await dialog.FillLastWorkingDayAsync(leavingDateRaw!);
        await dialog.ClickNextAsync();

        await dialog.SelectLeavingReasonAsync("Resignation");
        await dialog.ClickNextAsync();

        await dialog.ConfirmAsync();
        Assert.False(await dialog.IsVisibleAsync(),
            "Expected the Start Leaving Process dialog to close after a successful submission");

        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });
    }
}
