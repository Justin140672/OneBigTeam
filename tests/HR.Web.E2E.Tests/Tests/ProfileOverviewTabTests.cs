using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that the Overview tab on the self-service My Profile page
/// renders the employee's employment details and action buttons.
/// </summary>
public sealed class ProfileOverviewTabTests(HrSettingsSerialFixture fixture) : HrSettingsSerialTestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail = "tom.williams@acme.example";

    [Fact]
    public async Task OverviewTab_ShowsEmployeeJobTitle_AndActionButtons()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile  = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var overview = new OverviewTab(_page);

        // ── Step 1: Login as Tom ──────────────────────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        // ── Step 2: Navigate to Tom's self-service profile ────────────────────
        await profile.GoToAsync(AcmeId, TomId);

        // ── Step 3: The Overview tab should be the default tab ────────────────
        // If it is not default, open it explicitly.
        if (!await overview.IsVisibleAsync())
            await profile.OpenOverviewTabAsync();

        await overview.WaitForLoadAsync();

        // ── Step 4: Overview grid is rendered ────────────────────────────────
        Assert.True(await overview.IsVisibleAsync(),
            "Expected the overview-grid to be visible on the Overview tab");

        // ── Step 5: Key employment details are displayed ──────────────────────
        var jobTitle = await overview.GetDetailAsync("Job Title");
        Assert.False(string.IsNullOrWhiteSpace(jobTitle),
            "Expected a Job Title to be displayed in the overview");
        Assert.Contains("Software Engineer", jobTitle, StringComparison.OrdinalIgnoreCase);

        var department = await overview.GetDetailAsync("Department");
        Assert.False(string.IsNullOrWhiteSpace(department),
            "Expected a Department to be displayed in the overview");
        Assert.Contains("Engineering", department, StringComparison.OrdinalIgnoreCase);

        // ── Step 6: Action buttons are present ───────────────────────────────
        var content = await _page.ContentAsync();
        Assert.Contains("Request Leave", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OverviewTab_ShowsEmploymentFields_InExpectedOrder()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile  = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var overview = new OverviewTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);

        if (!await overview.IsVisibleAsync())
            await profile.OpenOverviewTabAsync();

        await overview.WaitForLoadAsync();

        // ── Every mandatory field renders with a non-empty value ──────────────
        var employeeNumber = await overview.GetDetailAsync("Employee Number");
        Assert.False(string.IsNullOrWhiteSpace(employeeNumber),
            "Expected an Employee Number to be displayed in the overview");

        var location = await overview.GetDetailAsync("Location");
        Assert.False(string.IsNullOrWhiteSpace(location),
            "Expected a Location to be displayed in the overview");

        var employmentType = await overview.GetDetailAsync("Employment Type");
        Assert.False(string.IsNullOrWhiteSpace(employmentType),
            "Expected an Employment Type to be displayed in the overview");

        var employmentStatus = await overview.GetDetailAsync("Employment Status");
        Assert.False(string.IsNullOrWhiteSpace(employmentStatus),
            "Expected an Employment Status to be displayed in the overview");
        Assert.Contains(employmentStatus, new[] { "Active", "Suspended", "Leaver" });

        var lengthOfService = await overview.GetDetailAsync("Length of Service");
        Assert.False(string.IsNullOrWhiteSpace(lengthOfService),
            "Expected a Length of Service to be displayed in the overview");

        var workingPattern = await overview.GetDetailAsync("Working Pattern");
        Assert.False(string.IsNullOrWhiteSpace(workingPattern),
            "Expected a Working Pattern to be displayed in the overview");
        Assert.Contains("hrs/day", workingPattern, StringComparison.OrdinalIgnoreCase);

        // ── Continuous Service Date is optional — only assert on shape when present ──
        var continuousServiceDate = await overview.GetDetailAsync("Continuous Service Date");
        if (continuousServiceDate is not null)
            Assert.False(string.IsNullOrWhiteSpace(continuousServiceDate));

        // ── Relative field order matches the required Employment card layout ──
        var labels = await overview.GetEmploymentCardLabelsAsync();
        var expectedOrder = new[]
        {
            "Job Title", "Employee Number", "Department", "Location", "Manager",
            "Employment Type", "Employment Status", "Start Date", "Length of Service", "Working Pattern",
        };

        var indices = expectedOrder.Select(label => labels.ToList().IndexOf(label)).ToList();
        Assert.All(indices, index => Assert.True(index >= 0, "Expected every mandatory Employment field label to be present"));
        Assert.True(indices.SequenceEqual(indices.OrderBy(i => i)),
            $"Expected Employment fields in order [{string.Join(", ", expectedOrder)}] but got [{string.Join(", ", labels)}]");
    }

    [Fact]
    public async Task OverviewTab_Salary_HiddenByDefault_ShownWhenCompanySettingEnabled()
    {
        var login      = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile    = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var overview   = new OverviewTab(_page);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        // "Display salary to employees on their profile" lives on the standalone HR Settings page
        // (HrSettingsPage.razor), gated on Session.IsHrAdministrator — it used to live on the
        // Company Settings tab (CompanyAdministrator-only) before the HR-policy fields were split
        // out, so this needs an HrAdministrator persona.
        const string hrAdminEmail = "laura.bennett@acme.example";

        // ── Baseline: Salary row is not rendered while the company setting is off ──
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);
        await profile.GoToAsync(AcmeId, TomId);
        if (!await overview.IsVisibleAsync())
            await profile.OpenOverviewTabAsync();
        await overview.WaitForLoadAsync();

        var wasEnabled = false;
        var salaryBeforeEnabling = await overview.GetDetailAsync("Salary");

        try
        {
            // ── Enable the setting as an HR administrator ───────────────────────
            await login.GoToAsync();
            await login.LoginAsync(hrAdminEmail);
            await hrSettings.GoToAsync(AcmeId);

            wasEnabled = await hrSettings.IsDisplaySalaryOnEmployeeProfileCheckedAsync();
            if (!wasEnabled)
            {
                await hrSettings.SetDisplaySalaryOnEmployeeProfileAsync(true);
                await hrSettings.SaveAsync();
                Assert.False(await hrSettings.HasErrorAsync(),
                    "Expected no error after enabling 'display salary on employee profile'");
            }
            else
            {
                // Setting was already enabled — the "hidden by default" baseline captured
                // above won't be meaningful, but we can still verify it's shown once enabled.
                Assert.False(string.IsNullOrWhiteSpace(salaryBeforeEnabling));
            }

            // ── Re-login as Tom so a fresh AppSession picks up the new setting ─
            await login.GoToAsync();
            await login.LoginAsync(TomEmail);
            await profile.GoToAsync(AcmeId, TomId);
            if (!await overview.IsVisibleAsync())
                await profile.OpenOverviewTabAsync();
            await overview.WaitForLoadAsync();

            if (!wasEnabled)
                Assert.Null(salaryBeforeEnabling);

            var salaryAfterEnabling = await overview.GetDetailAsync("Salary");
            Assert.False(string.IsNullOrWhiteSpace(salaryAfterEnabling),
                "Expected a Salary row to be displayed once 'display salary on employee profile' is enabled");
        }
        finally
        {
            // ── Restore the original setting so this test doesn't leak state ───
            if (!wasEnabled)
            {
                await login.GoToAsync();
                await login.LoginAsync(hrAdminEmail);
                await hrSettings.GoToAsync(AcmeId);
                await hrSettings.SetDisplaySalaryOnEmployeeProfileAsync(false);
                await hrSettings.SaveAsync();
            }
        }
    }

    [Fact]
    public async Task OverviewTab_RequestLeaveButton_OpensLeaveDialog()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile  = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var overview = new OverviewTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);

        if (!await overview.IsVisibleAsync())
            await profile.OpenOverviewTabAsync();

        await overview.WaitForLoadAsync();

        // ── Clicking "Request Leave" from the overview opens the leave dialog ─
        await overview.ClickRequestLeaveAsync();
        await _page.WaitForSelectorAsync(".e-dialog", new() { Timeout = 10_000 });

        Assert.True(await _page.Locator(".e-dialog").IsVisibleAsync(),
            "Expected the leave request dialog to open after clicking 'Request Leave' from the Overview tab");
    }

    [Fact]
    public async Task OverviewTab_ShowsOpenTasksStatCard()
    {
        // Tom has no onboarding plan (see EmployeeOnboardingTabTests remarks — only employees
        // created via the CreateEmployee handler get one, and no seeded employee has one), so
        // the "Onboarding Progress" card should also be absent for him; the Open Tasks stat
        // card, however, always renders regardless of onboarding state.
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile  = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var overview = new OverviewTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);

        if (!await overview.IsVisibleAsync())
            await profile.OpenOverviewTabAsync();

        await overview.WaitForLoadAsync();

        var openTasks = await overview.GetStatValueAsync("Open Tasks");
        Assert.False(string.IsNullOrWhiteSpace(openTasks),
            "Expected an Open Tasks stat card to be displayed in the overview");
        Assert.True(int.TryParse(openTasks, out var count) && count >= 0,
            $"Expected the Open Tasks stat value to be a non-negative integer, got '{openTasks}'");

        Assert.False(await overview.HasOnboardingProgressCardAsync(),
            "Expected no Onboarding Progress card for Tom, who has no onboarding plan");
    }

    [Theory]
    [InlineData("Open Tasks", "Tasks")]
    [InlineData("Leave Remaining", "Leave")]
    [InlineData("Pending Requests", "Leave")]
    [InlineData("Sickness Absence", "Sickness")]
    public async Task OverviewTab_ClickingStatCard_NavigatesToRelevantTab(string statLabel, string expectedTabName)
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile  = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var overview = new OverviewTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);

        if (!await overview.IsVisibleAsync())
            await profile.OpenOverviewTabAsync();

        await overview.WaitForLoadAsync();

        await overview.ClickStatCardAsync(statLabel);

        Assert.Equal(expectedTabName, await profile.GetActiveTabNameAsync());
    }

    [Fact]
    public async Task OverviewTab_NotifySicknessButton_OpensSicknessDialog()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile  = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var overview = new OverviewTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);

        if (!await overview.IsVisibleAsync())
            await profile.OpenOverviewTabAsync();

        await overview.WaitForLoadAsync();

        // ── Clicking "Notify Sickness" from the overview opens the self-service
        // sickness dialog (RecordSicknessDialog in SelfService mode) ────────────
        await overview.ClickNotifySicknessAsync();

        Assert.True(await _page.Locator("[role='dialog'].record-sickness-dialog").IsVisibleAsync(),
            "Expected the sickness notification dialog to open after clicking 'Notify Sickness' from the Overview tab");
        Assert.Contains("Notify Sickness", await _page.ContentAsync());
    }
}
