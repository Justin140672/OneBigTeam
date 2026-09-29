using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class ProfileTasksTabTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail = "tom.williams@acme.example";

    private const string TomTaskFragment = "probation review";

    [Fact]
    public async Task TasksTab_ShowsAssignedTasks_ForEmployee()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenTasksTabAsync();

        await _page.WaitForSelectorAsync(".e-grid, .task-cell, p",
            new() { Timeout = 15_000 });

        var content = await _page.ContentAsync();
        Assert.Contains(TomTaskFragment, content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TasksTab_ClickingTask_OpensTaskDialog_WithoutNavigatingAway()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenTasksTabAsync();

        await _page.WaitForSelectorAsync(".e-grid, .task-cell", new() { Timeout = 15_000 });

        var profileUrlBeforeClick = _page.Url;

        await _page.Locator(".e-row").First.Locator("button[title='View']").ClickAsync();

        await _page.WaitForSelectorAsync("[role='dialog'].task-view-dialog", new() { Timeout = 15_000 });
        Assert.True(await _page.Locator("[role='dialog'].task-view-dialog").IsVisibleAsync(),
            "Expected clicking View on My Profile's Tasks tab to open the task in a dialog");
        Assert.Equal(profileUrlBeforeClick, _page.Url);
    }
}
