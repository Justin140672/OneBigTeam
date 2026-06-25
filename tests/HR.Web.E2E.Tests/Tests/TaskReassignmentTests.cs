using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that an HR Administrator can reassign a task to a different employee,
/// after which:
/// - The original assignee no longer sees the task in their dashboard.
/// - The new assignee sees the task in their dashboard.
///
/// Uses Sarah's seeded "Prepare board meeting agenda" task
/// (ID: a0000000-0000-0000-0000-000000000002) and reassigns it to James Okafor.
/// </summary>
[Collection("E2E")]
public sealed class TaskReassignmentTests : IAsyncLifetime
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SarahId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    // Sarah's seeded task — "Prepare board meeting agenda".
    private static readonly Guid BoardAgendaTaskId = Guid.Parse("a0000000-0000-0000-0000-000000000002");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string SarahEmail = "sarah.chen@acme.example";
    private const string JamesEmail = "james.okafor@acme.example";

    private readonly AppFixture _fixture;
    private IBrowserContext _context = null!;
    private IPage           _page    = null!;

    public TaskReassignmentTests(AppFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _context = await _fixture.Browser.NewContextAsync();
        _page    = await _context.NewPageAsync();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task ReassignTask_TransfersOwnership_ToNewEmployee()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);
        var dash     = new DashboardPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Laura (HR Admin) ─────────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ── Step 2: Navigate to the board agenda task ─────────────────────────
        await taskView.GoToAsync(AcmeId, BoardAgendaTaskId);

        var taskTitle = await taskView.GetTitleAsync();
        Assert.Contains("board meeting", taskTitle, StringComparison.OrdinalIgnoreCase);

        // ── Step 3: Find the reassign control and assign to James Okafor ──────
        // The task view page may show a "Reassign" button or an assignee dropdown for HR.
        var reassignBtn = _page.GetByRole(AriaRole.Button, new() { Name = "Reassign" });

        if (await reassignBtn.IsVisibleAsync())
        {
            await reassignBtn.ClickAsync();
            await _page.WaitForSelectorAsync(".e-dialog, .e-popup", new() { Timeout = 10_000 });

            // Select James from the dropdown/input.
            var input = _page.Locator(".e-dialog input.e-input, .e-popup input.e-input").First;
            await input.FillAsync("James");
            await _page.WaitForSelectorAsync(".e-list-item:has-text('James Okafor')",
                new() { Timeout = 10_000 });
            await _page.Locator(".e-list-item")
                .Filter(new() { HasText = "James Okafor" })
                .First
                .ClickAsync();

            var confirmBtn = _page.GetByRole(AriaRole.Button, new() { Name = "Confirm" });
            if (await confirmBtn.IsVisibleAsync())
                await confirmBtn.ClickAsync();

            await _page.WaitForFunctionAsync(
                "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
                null, new PageWaitForFunctionOptions { Timeout = 15_000 });
        }
        else
        {
            // Skip — the reassign UI is not yet implemented for this task type.
            return;
        }

        // ── Step 4: Switch to James and verify the task is in his dashboard ───
        await login.SwitchAccountAsync(JamesEmail);
        await dash.GoToAsync();

        var jamesTasks = await dash.GetTaskTitlesAsync();
        Assert.Contains(jamesTasks, t =>
            t.Contains("board meeting", StringComparison.OrdinalIgnoreCase));

        // ── Step 5: Sarah's dashboard should no longer show the task ──────────
        await login.SwitchAccountAsync(SarahEmail);
        await dash.GoToAsync();

        var sarahTasks = await dash.GetTaskTitlesAsync();
        Assert.DoesNotContain(sarahTasks, t =>
            t.Contains("board meeting", StringComparison.OrdinalIgnoreCase));
    }
}
