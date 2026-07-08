using System.Globalization;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the "Leave Balance Adjustment" feature: the hours-based "Current Balance" / "TOIL
/// Balance" cards on the admin employee Leave tab (<c>EmployeeLeaveTab.razor</c>), the
/// <c>AdjustLeaveBalanceDialog</c> used to create adjustments, and the permission boundaries
/// around who can see/use the Adjust control.
///
/// Seeded data used (see HR.Modules.Leave.LeaveModule.SeedLeaveAsync and
/// HR.Modules.Identity.IdentityModule.SeedDevUserAsync):
///   - Acme company 00000000-0000-0000-0000-000000000001.
///   - Laura Bennett (HR Administrator, laura.bennett@acme.example,
///     30000000-0000-0000-0000-000000000005) — performs all admin actions below and also has her
///     own linked employee/leave-balance record, used for the self-service boundary test.
///   - Tom Williams (Employee, tom.williams@acme.example, 30000000-0000-0000-0000-000000000004)
///     and Sarah Chen (Company Administrator, 30000000-0000-0000-0000-000000000001) both have
///     seeded balances (including Annual Leave, Sick Leave and TOIL) for the current policy year.
///   - James Okafor (Employee + Manager, james.okafor@acme.example,
///     30000000-0000-0000-0000-000000000002) holds "leave:approve" but not "leave:manage"/
///     "employee:manage" — used for the permission-boundary test.
///
/// IMPORTANT — deviation from the original feature spec: the admin employee edit page
/// (EmployeeEdit.razor) redirects any signed-in user without Session.CanManageEmployees (i.e.
/// without the HR Administrator/Company Administrator "employee:manage" permission) away from
/// /companies/{companyId}/employees/{employeeId} entirely — see EmployeeEdit.razor's
/// `if (!Session.CanManageEmployees) Navigation.NavigateTo(Session.MyProfileUrl, ...)` and the
/// existing UnauthorizedAccessTests.Employee_CannotAccess_AnotherEmployeesAdminProfile test. This
/// means a Manager (like James) cannot reach the admin Leave tab for another employee at all —
/// not merely "tab visible, Adjust button hidden" as originally assumed. The permission-boundary
/// test below asserts the actual (stronger) behavior: James is redirected away before the tab, and
/// therefore the Adjust control, ever renders.
/// </summary>
[Collection("E2E")]
public sealed class LeaveBalanceAdjustmentTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId   = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid SarahId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid LauraId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string JamesEmail = "james.okafor@acme.example";

    private static decimal ParseHours(string text) =>
        decimal.Parse(text.TrimEnd('h', 'H'), NumberStyles.Number, CultureInfo.InvariantCulture);

    // ── 1. Loading the page ──────────────────────────────────────────────────

    [Fact]
    public async Task AdminLeaveTab_ShowsBalances_InHours_NotDays()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenLeaveTabAsync();

        var annualLeaveText = await empAdmin.GetBalanceRowTextAsync("Annual Leave");
        Assert.NotNull(annualLeaveText);
        Assert.EndsWith("h", annualLeaveText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("day", annualLeaveText, StringComparison.OrdinalIgnoreCase);

        var toilText = await empAdmin.GetToilBalanceTextAsync();
        Assert.NotNull(toilText);
        Assert.EndsWith("h", toilText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminLeaveTab_EmployeeWithNoLeavePolicyAssignment_ShowsNaAndNoAdjustButtons()
    {
        // An employee created without a Position Profile gets no DefaultLeavePolicyId, so
        // HR.Modules.Leave's EmployeeCreatedHandler never creates a policy assignment or any
        // LeaveBalance rows for them (see EmployeeCreatedHandler.HandleAsync: it returns early
        // when integrationEvent.DefaultLeavePolicyId is null and no assignment already exists).
        // That deterministically reproduces the "n/a" / HasBalance == false row for every leave
        // type, unlike relying on a specific seeded leave type name.
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList  = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit  = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"NoLeave{unique}";
        var workEmail = $"e2e.noleave{unique}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();

        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        // Deliberately no Position Profile selected.
        await empEdit.SaveNewEmployeeAsync();

        await empList.ClickEmployeeAsync(lastName);
        await _page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });

        await empAdmin.OpenLeaveTabAsync();

        Assert.Equal("n/a", await empAdmin.GetBalanceRowTextAsync("Annual Leave"));
        Assert.False(await empAdmin.HasAdjustButtonAsync("Annual Leave"),
            "A leave type row with no balance must never show an Adjust button");

        Assert.Equal("n/a", await empAdmin.GetToilBalanceTextAsync());
        Assert.False(await empAdmin.HasToilAdjustButtonAsync(),
            "The TOIL card must never show an Adjust button when the employee has no TOIL balance");
    }

    // ── 2. Adjust button visibility for HR Administrator ─────────────────────

    [Fact]
    public async Task AdminLeaveTab_AdjustButton_VisibleToHrAdministrator_ForBalanceRow()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, SarahId);
        await empAdmin.OpenLeaveTabAsync();

        Assert.True(await empAdmin.HasAdjustButtonAsync("Annual Leave"),
            "Expected Laura (HR Administrator) to see the Adjust button on a leave type row with a balance");
    }

    // ── 3. Creating an adjustment ─────────────────────────────────────────────

    [Fact]
    public async Task AdjustDialog_PositiveAdjustment_IncreasesBalance_AndClosesDialog()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenLeaveTabAsync();

        var before = ParseHours((await empAdmin.GetBalanceRowTextAsync("Annual Leave"))!);

        await empAdmin.OpenAdjustDialogAsync("Annual Leave");
        await empAdmin.SubmitAdjustmentAsync(
            "Annual Leave", hours: 7.5m, reason: "Manual Award", comments: "E2E test award");

        Assert.False(await empAdmin.IsAdjustDialogVisibleAsync("Annual Leave"),
            "Expected the Adjust dialog to close after a successful adjustment");

        var after = ParseHours((await empAdmin.GetBalanceRowTextAsync("Annual Leave"))!);
        Assert.Equal(before + 7.5m, after, precision: 1);
    }

    // ── 4. Validation failure surfaces inline ────────────────────────────────

    [Fact]
    public async Task AdjustDialog_ZeroHours_ShowsInlineError_AndStaysOpen()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenLeaveTabAsync();

        await empAdmin.OpenAdjustDialogAsync("Sick Leave");
        await empAdmin.SubmitAdjustmentAsync("Sick Leave", hours: 0m);

        Assert.True(await empAdmin.IsAdjustDialogVisibleAsync("Sick Leave"),
            "Expected the dialog to stay open after submitting a zero-hours adjustment");

        var error = await empAdmin.GetAdjustDialogErrorAsync("Sick Leave");
        Assert.NotNull(error);
        Assert.Contains("non-zero", error, StringComparison.OrdinalIgnoreCase);
    }

    // ── 5. Negative adjustment below zero rejected without override, then succeeds with override ──

    [Fact]
    public async Task AdjustDialog_NegativeBelowZero_RejectedWithoutOverride_ThenSucceedsWithOverride()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, SarahId);
        await empAdmin.OpenLeaveTabAsync();

        await empAdmin.OpenAdjustDialogAsync("Annual Leave");

        // Wildly overshoot any plausible remaining balance so this deterministically goes
        // below zero regardless of prior test runs against the same seeded balance.
        await empAdmin.SubmitAdjustmentAsync(
            "Annual Leave", hours: -100_000m, reason: "Manual Deduction",
            comments: "E2E overshoot without override", allowNegativeOverride: false);

        Assert.True(await empAdmin.IsAdjustDialogVisibleAsync("Annual Leave"),
            "Expected the dialog to stay open when a negative adjustment would take the balance below zero");

        var error = await empAdmin.GetAdjustDialogErrorAsync("Annual Leave");
        Assert.NotNull(error);
        Assert.Contains("below zero", error, StringComparison.OrdinalIgnoreCase);

        // Retry with the override checked — the hours/reason/comments are still populated from
        // the failed attempt (the dialog only resets on Cancel or success), so only the override
        // checkbox needs to change.
        await empAdmin.SubmitAdjustmentAsync(
            "Annual Leave", hours: -160m, allowNegativeOverride: true);

        Assert.False(await empAdmin.IsAdjustDialogVisibleAsync("Annual Leave"),
            "Expected the dialog to close once the negative-balance override is checked");

        var after = ParseHours((await empAdmin.GetBalanceRowTextAsync("Annual Leave"))!);
        Assert.True(after < 0, $"Expected the balance to be driven below zero, but got {after}h");
    }

    // ── 6. Cancel dismisses the dialog without submitting ────────────────────

    [Fact]
    public async Task AdjustDialog_Cancel_DismissesWithoutSubmitting_AndBalanceUnchanged()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenLeaveTabAsync();

        var before = await empAdmin.GetBalanceRowTextAsync("Annual Leave");

        await empAdmin.OpenAdjustDialogAsync("Annual Leave");
        await empAdmin.FillAdjustmentAmountAsync("Annual Leave", 50m);
        await empAdmin.CloseAdjustDialogAsync("Annual Leave");

        Assert.False(await empAdmin.IsAdjustDialogVisibleAsync("Annual Leave"),
            "Expected the dialog to close after clicking Cancel");

        var after = await empAdmin.GetBalanceRowTextAsync("Annual Leave");
        Assert.Equal(before, after);
    }

    // ── 7. Permission boundary ────────────────────────────────────────────────

    [Fact]
    public async Task ManagerRole_IsRedirectedAway_FromAdminEmployeeEditPage_AndNeverReachesAdjustControl()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);

        // James holds the Manager role (leave:approve) but not employee:manage/leave:manage.
        // EmployeeEdit.razor's own LoadAsync redirects any user without Session.CanManageEmployees
        // away from the admin edit route before any tab (including Leave) renders — mirroring
        // UnauthorizedAccessTests.Employee_CannotAccess_AnotherEmployeesAdminProfile. This
        // confirms a Manager cannot reach the Adjust control for another employee via this page.
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees/{TomId}");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var finalUrl = _page.Url;
        Assert.DoesNotContain($"/employees/{TomId}", finalUrl);
    }

    // ── 8. Self-service employee never sees Adjust ───────────────────────────

    [Fact]
    public async Task MyProfileLeaveTab_NeverShowsAdjustButton_EvenForHrAdministratorViewingOwnProfile()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Laura is an HR Administrator (CanManageEmployees == true) but is viewing her own
        // self-service "My Profile" Leave tab here, not the admin edit page — the Adjust control
        // must never appear there regardless of the viewer's role.
        await profile.GoToAsync(AcmeId, LauraId);
        await profile.OpenLeaveTabAsync();

        var annualLeaveText = await profile.GetAnnualLeaveRemainingTextAsync();
        Assert.NotNull(annualLeaveText);
        Assert.Contains("h", annualLeaveText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("day", annualLeaveText, StringComparison.OrdinalIgnoreCase);

        Assert.False(await profile.HasAnyAdjustButtonOnLeaveTabAsync(),
            "Laura must not see an Adjust control on her own My Profile Leave tab, even though " +
            "she can adjust other employees' balances from the admin edit page");
    }
}
