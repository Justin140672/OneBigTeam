using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Tasks tab on the self-service My Profile page:
/// - Seeded tasks are listed for the employee.
/// - Clicking a task navigates to the Task View page.
/// </summary>
[Collection("E2E")]
public sealed class ProfileTasksTabTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail = "tom.williams@acme.example";

    // Tom has a seeded "Schedule probation review" task assigned to him.
    private const string TomTaskFragment = "probation review";

    [Fact]
    public async Task TasksTab_ShowsAssignedTasks_ForEmployee()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Tom ──────────────────────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        // ── Step 2: Navigate to Tom's profile and open the Tasks tab ──────────
        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenTasksTabAsync();

        // Wait for the task grid or empty state.
        await _page.WaitForSelectorAsync(".e-grid, .task-cell, p",
            new() { Timeout = 15_000 });

        // ── Step 3: Tom's probation task should be listed ─────────────────────
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

        // The View toolbar button starts disabled (e-overlay) until a row is selected.
        // Always select the first row first, then wait for Syncfusion to enable the button.
        await _page.Locator(".e-row").First.ClickAsync();
        await _page.WaitForFunctionAsync(
            "!document.querySelector('[id=\"hr-view\"]')?.classList?.contains('e-overlay')",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        var profileUrlBeforeClick = _page.Url;
        await _page.Locator("[id='hr-view']").ClickAsync();

        // Should open the task in a dialog (TaskViewDialog), not navigate to /tasks/{id}.
        // Scoped to [role='dialog'] because Syncfusion's SfDialog CssClass propagates onto
        // multiple elements (the outer container, the dialog itself, and the close button),
        // which makes a bare ".task-view-dialog" locator ambiguous under Playwright's strict mode.
        await _page.WaitForSelectorAsync("[role='dialog'].task-view-dialog", new() { Timeout = 15_000 });
        Assert.True(await _page.Locator("[role='dialog'].task-view-dialog").IsVisibleAsync(),
            "Expected clicking View on My Profile's Tasks tab to open the task in a dialog");
        Assert.Equal(profileUrlBeforeClick, _page.Url);
    }
}
