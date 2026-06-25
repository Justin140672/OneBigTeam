using System.Text.RegularExpressions;
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
public sealed class ProfileTasksTabTests : IAsyncLifetime
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail = "tom.williams@acme.example";

    // Tom has a seeded "Schedule probation review" task assigned to him.
    private const string TomTaskFragment = "probation review";

    private readonly AppFixture _fixture;
    private IBrowserContext _context = null!;
    private IPage           _page    = null!;

    public ProfileTasksTabTests(AppFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _context = await _fixture.Browser.NewContextAsync();
        _page    = await _context.NewPageAsync();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

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
    public async Task TasksTab_ClickingTask_NavigatesToTaskViewPage()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenTasksTabAsync();

        await _page.WaitForSelectorAsync(".e-grid, .task-cell", new() { Timeout = 15_000 });

        // Click the "View" toolbar button on the first task row.
        // Syncfusion grid toolbar action with id 'hr-view' opens the task.
        var viewBtn = _page.Locator("[id='hr-view'], [title='View task']").First;

        if (!await viewBtn.IsVisibleAsync())
        {
            // Fallback: select the first row then click the toolbar button.
            await _page.Locator(".e-row").First.ClickAsync();
            viewBtn = _page.Locator("[id='hr-view'], [title='View task']").First;
        }

        await viewBtn.ClickAsync();

        // Should navigate to /tasks/{id}
        await _page.WaitForURLAsync(new Regex("/tasks/"), new() { Timeout = 15_000 });
        Assert.Matches(new Regex("/tasks/[0-9a-f-]{36}"), _page.Url);
    }
}
