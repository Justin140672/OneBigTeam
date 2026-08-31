using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// ADM-04 — "Administration settings hub". One class per persona (each binding its own cached
/// storageState fixture — see RolePersonaFixtureBase / OutlierPersonaFixtures). Covers:
/// - the category cards visible per persona match the ADM-05 role-separation matrix (presence + absence);
/// - a persona with no visible category is redirected to /access-denied when hitting the hub URL;
/// - clicking a category link from the hub resolves to the expected settings route;
/// - the "Administration" breadcrumb link on a settings screen returns to the hub;
/// - the "Not yet configurable" marker is shown for at least one persona.
///
/// Redirect waits follow AdministrativeRoleSeparationTests: the guard redirect is a client-side
/// Blazor NavigateTo, so poll the URL rather than trusting NetworkIdle.
/// </summary>
file static class Acme
{
    public static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
}

// ─────────────────────────────────────────────────────────────────────────────
// HR Administrator — Laura Bennett.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class AdministrationHubHrAdminTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private const string Email = "laura.bennett@acme.example";

    private async Task LoginAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(Email);
    }

    [Fact]
    public async Task HrAdmin_SeesHrCategories_ButNotRecruitment()
    {
        await LoginAsync();
        var hub = new AdministrationHubPage(_page, _fixture.WebBaseUrl);
        await hub.GoToAsync(Acme.Id);

        Assert.True(await hub.HasCategoryAsync("Leave"));
        Assert.True(await hub.HasCategoryAsync("Documents"));
        Assert.True(await hub.HasCategoryAsync("Notifications"));
        Assert.True(await hub.HasCategoryAsync("Probation"));
        Assert.True(await hub.HasCategoryAsync("Subscription"));

        // ADM-05: HR Administrator holds no company:manage, so company-config categories stay hidden.
        Assert.False(await hub.HasCategoryAsync("Company profile and addresses"));
        Assert.False(await hub.HasCategoryAsync("Company defaults"));
        Assert.False(await hub.HasCategoryAsync("Recruitment"));
    }

    [Fact]
    public async Task HrAdmin_SeesNotYetConfigurableMarker()
    {
        await LoginAsync();
        var hub = new AdministrationHubPage(_page, _fixture.WebBaseUrl);
        await hub.GoToAsync(Acme.Id);

        Assert.True(await hub.HasNotYetConfigurableMarkerAsync(),
            "HR Admin should see a 'Not yet configurable' marker on the hub (e.g. Notifications settings)");
    }

    [Fact]
    public async Task HrAdmin_ClickingLeaveTypes_ResolvesToLeaveTypesRoute()
    {
        await LoginAsync();
        var hub = new AdministrationHubPage(_page, _fixture.WebBaseUrl);
        await hub.GoToAsync(Acme.Id);

        await hub.ClickLinkAsync("Leave Types");
        await _page.WaitForURLAsync($"**/companies/{Acme.Id}/leave-types", new() { Timeout = 15_000 });

        Assert.EndsWith($"/companies/{Acme.Id}/leave-types", _page.Url.Split('?')[0].TrimEnd('/'));
    }

    [Fact]
    public async Task LeaveTypesScreen_AdministrationBreadcrumb_ReturnsToHub()
    {
        await LoginAsync();
        var leaveTypes = new LeaveTypeListPage(_page, _fixture.WebBaseUrl);
        await leaveTypes.GoToAsync(Acme.Id);

        var hub = new AdministrationHubPage(_page, _fixture.WebBaseUrl);
        Assert.True(await hub.BreadcrumbHasAdministrationAsync(),
            "The Administration breadcrumb crumb should be present on the Leave Types screen");

        await hub.ClickAdministrationBreadcrumbAsync();
        await _page.WaitForURLAsync($"**/companies/{Acme.Id}/administration", new() { Timeout = 15_000 });

        Assert.EndsWith($"/companies/{Acme.Id}/administration", _page.Url.Split('?')[0].TrimEnd('/'));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Recruiter — Marcus Diallo. Only the Recruitment category.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class AdministrationHubRecruiterTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private const string Email = "marcus.diallo@acme.example";

    private async Task LoginAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(Email);
    }

    [Fact]
    public async Task Recruiter_SeesOnlyRecruitmentCategory()
    {
        await LoginAsync();
        var hub = new AdministrationHubPage(_page, _fixture.WebBaseUrl);
        await hub.GoToAsync(Acme.Id);

        Assert.True(await hub.HasCategoryAsync("Recruitment"));

        Assert.False(await hub.HasCategoryAsync("Company profile and addresses"));
        Assert.False(await hub.HasCategoryAsync("Company defaults"));
        Assert.False(await hub.HasCategoryAsync("Leave"));
        Assert.False(await hub.HasCategoryAsync("Documents"));
        Assert.False(await hub.HasCategoryAsync("Notifications"));
        Assert.False(await hub.HasCategoryAsync("Probation"));
        Assert.False(await hub.HasCategoryAsync("Subscription"));
    }

    [Fact]
    public async Task Recruiter_ClickingRecruitmentStages_ResolvesToRoute()
    {
        await LoginAsync();
        var hub = new AdministrationHubPage(_page, _fixture.WebBaseUrl);
        await hub.GoToAsync(Acme.Id);

        await hub.ClickLinkAsync("Recruitment Stages");
        await _page.WaitForURLAsync($"**/companies/{Acme.Id}/recruitment-stages", new() { Timeout = 15_000 });

        Assert.EndsWith($"/companies/{Acme.Id}/recruitment-stages", _page.Url.Split('?')[0].TrimEnd('/'));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Manager — James Okafor. No manage capabilities => empty hub => /access-denied.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class AdministrationHubManagerTests(ManagerPersonaFixture fixture)
    : RoleE2ETestBase<ManagerPersonaFixture>(fixture)
{
    private const string Email = "james.okafor@acme.example";

    [Fact]
    public async Task Manager_HittingHubUrl_IsRedirectedToAccessDenied()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(Email);

        var hub = new AdministrationHubPage(_page, _fixture.WebBaseUrl);
        await _page.GotoAsync(hub.RouteFor(Acme.Id));
        await WaitForUrlToStopContainingAsync("/administration");

        var accessDenied = new AccessDeniedPage(_page, _fixture.WebBaseUrl);
        Assert.True(accessDenied.IsOnRoute,
            $"Expected the Manager persona (no admin categories) to be redirected to /access-denied, was: {_page.Url}");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Company-Administrator-only — Priya Shah. Company + Subscription categories only.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class AdministrationHubCompanyAdminTests(PriyaShahPersonaFixture fixture)
    : RoleE2ETestBase<PriyaShahPersonaFixture>(fixture)
{
    private const string Email = "priya.shah@acme.example";

    private async Task LoginAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(Email);
    }

    [Fact]
    public async Task CompanyAdminOnly_SeesCompanyAndSubscription_ButNoHrOrRecruitment()
    {
        await LoginAsync();
        var hub = new AdministrationHubPage(_page, _fixture.WebBaseUrl);
        await hub.GoToAsync(Acme.Id);

        Assert.True(await hub.HasCategoryAsync("Company profile and addresses"));
        Assert.True(await hub.HasCategoryAsync("Company defaults"));
        Assert.True(await hub.HasCategoryAsync("Subscription"));

        Assert.False(await hub.HasCategoryAsync("Leave"));
        Assert.False(await hub.HasCategoryAsync("Recruitment"));
        Assert.False(await hub.HasCategoryAsync("Documents"));
        Assert.False(await hub.HasCategoryAsync("Notifications"));
        Assert.False(await hub.HasCategoryAsync("Probation"));
    }

    [Fact]
    public async Task CompanyAdminOnly_ClickingCompanyProfile_ResolvesToCompanyEdit()
    {
        await LoginAsync();
        var hub = new AdministrationHubPage(_page, _fixture.WebBaseUrl);
        await hub.GoToAsync(Acme.Id);

        await hub.ClickLinkAsync("Company profile & addresses");
        await _page.WaitForURLAsync($"**/companies/{Acme.Id}/edit", new() { Timeout = 15_000 });

        Assert.EndsWith($"/companies/{Acme.Id}/edit", _page.Url.Split('?')[0].TrimEnd('/'));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Combined CompanyAdmin + Manager — Sarah Chen. Same hub surface as CompanyAdmin-only.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class AdministrationHubCompanyAdminPlusManagerTests(SarahChenPersonaFixture fixture)
    : RoleE2ETestBase<SarahChenPersonaFixture>(fixture)
{
    private const string Email = "sarah.chen@acme.example";

    [Fact]
    public async Task CompanyAdminPlusManager_SeesCompanyAndSubscription_ButNoHrAdminCategories()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(Email);

        var hub = new AdministrationHubPage(_page, _fixture.WebBaseUrl);
        await hub.GoToAsync(Acme.Id);

        Assert.True(await hub.HasCategoryAsync("Company profile and addresses"));
        Assert.True(await hub.HasCategoryAsync("Company defaults"));
        Assert.True(await hub.HasCategoryAsync("Subscription"));

        Assert.False(await hub.HasCategoryAsync("Notifications"));
        Assert.False(await hub.HasCategoryAsync("Probation"));
        Assert.False(await hub.HasCategoryAsync("Recruitment"));
    }
}
