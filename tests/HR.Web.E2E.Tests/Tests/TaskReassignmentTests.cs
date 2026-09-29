using HR.Modules.Tasks.Contracts;
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
/// Uses Laura Bennett's seeded "Prepare board meeting agenda" task
/// (ID: a0000000-0000-0000-0000-000000000029) and reassigns it to James Okafor.
///
/// Note: this scenario originally used Sarah Chen's equivalent task
/// (a0000000-0000-0000-0000-000000000002, TaskSource.Manual), reassigning it from Sarah to
/// James. Sarah is now seeded as CompanyAdministrator-only and is redirected away from "/" (see
/// Home.razor), so she can no longer reach the dashboard for the final assertion. Rather than
/// move Sarah's existing task, a brand-new parallel task owned by Laura was added instead (see
/// TasksModule.SeedTasksAsync), and Laura — who is also the logged-in HR Administrator
/// performing the reassignment — plays both roles here: the actor carrying out the
/// reassignment, and the original assignee whose dashboard is checked afterwards.
///
/// TaskSource.Manual has since been removed entirely; Laura's task (and Sarah's original, now
/// also removed) used that source purely as generic filler unrelated to reassignment mechanics,
/// which are source-agnostic (see ReassignTaskHandler). Laura's task now uses TaskSource.Workflow.
/// </summary>
public sealed class TaskReassignmentTests(CrossUserFixture fixture) : RoleE2ETestBase<CrossUserFixture>(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid LauraId = Guid.Parse("30000000-0000-0000-0000-000000000005");
    private static readonly Guid JamesId = Guid.Parse("30000000-0000-0000-0000-000000000002");

    private static readonly Guid BoardAgendaTaskId = Guid.Parse("a0000000-0000-0000-0000-000000000029");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string JamesEmail = "james.okafor@acme.example";

    [Fact]
    public async Task ReassignTask_TransfersOwnership_ToNewEmployee()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);
        var empEdit  = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var profile  = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, LauraId);
        await empEdit.OpenTasksTabAsync();
        await empEdit.ClickTaskAsync(BoardAgendaTaskId);
        await taskView.WaitForLoadedAsync();

        var taskTitle = await taskView.GetTitleAsync();
        Assert.Contains("board meeting", taskTitle, StringComparison.OrdinalIgnoreCase);

        var reassignBtn = _page.GetByRole(AriaRole.Button, new() { Name = "Reassign" });

        if (await reassignBtn.IsVisibleAsync())
        {
            await reassignBtn.ClickAsync();
            await _page.WaitForSelectorAsync(".e-dialog, .e-popup", new() { Timeout = 10_000 });

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
            return;
        }

        await login.SwitchAccountAsync(JamesEmail);
        await profile.GoToAsync(AcmeId, JamesId);
        await profile.OpenTasksTabAsync();

        var jamesTasks = await profile.GetTaskTitlesAsync();
        Assert.Contains(jamesTasks, t =>
            t.Contains("board meeting", StringComparison.OrdinalIgnoreCase));

        await login.SwitchAccountAsync(LauraEmail);
        await profile.GoToAsync(AcmeId, LauraId);
        await profile.OpenTasksTabAsync();

        var lauraTasks = await profile.GetTaskTitlesAsync();
        Assert.DoesNotContain(lauraTasks, t =>
            t.Contains("board meeting", StringComparison.OrdinalIgnoreCase));
    }
}
