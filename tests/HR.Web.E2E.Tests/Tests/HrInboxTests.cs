using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the HR Inbox (unassigned task queue).
/// Tom submits a personal-details change request which creates an unassigned task.
/// The HR Manager can then claim it, after which it disappears from the inbox
/// and appears in their own task list.
/// </summary>
public sealed class HrInboxTests(CrossUserFixture fixture) : CrossUserDocumentsAndRequestsTestBase(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId   = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid LauraId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    private const string TomEmail   = "tom.williams@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task HrInbox_Shows_Unassigned_Task_And_Claim_Removes_It_From_Inbox()
    {
        var uniqueNotes = $"E2E-Inbox-{Guid.NewGuid():N}";

        var login           = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile         = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var personalDetails = new PersonalDetailsTab(_page);
        var inbox           = new HrInboxPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Tom and submit a personal-details change request ──
        // This creates an unassigned HR task that appears in the inbox.
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenPersonalDetailsTabAsync();
        await personalDetails.WaitForLoadAsync();
        await personalDetails.ClickRequestChangeAsync();
        await personalDetails.FillChangeRequestNotesAsync(uniqueNotes);
        await personalDetails.SubmitChangeRequestAsync();

        // ── Step 2: Switch to Laura (HR Manager) ─────────────────────────────
        await login.SwitchAccountAsync(LauraEmail);

        // ── Step 3: Navigate to the HR Inbox ──────────────────────────────────
        await inbox.GoToAsync(AcmeId);

        Assert.False(await inbox.IsEmptyAsync(),
            "HR inbox should not be empty after Tom submitted a personal-details change request");

        var titles = await inbox.GetTaskTitlesAsync();
        Assert.Contains(titles,
            t => t.Contains("Tom Williams", StringComparison.OrdinalIgnoreCase) ||
                 t.Contains("Personal Details", StringComparison.OrdinalIgnoreCase));

        // ── Step 4: Claim the task ─────────────────────────────────────────────
        var taskTitle = titles.First(t =>
            t.Contains("Tom Williams",    StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Personal Details",StringComparison.OrdinalIgnoreCase));

        // This title is fixed/generic with no per-request suffix, so — same risk already
        // documented on HrInboxPage.ClaimAsync — more than one stray/unclaimed card elsewhere in
        // the shared inbox (e.g. from PersonalDetailsChangeRequestTests/PersonalDetailsTabTests,
        // which also submit personal-details change requests for Tom and don't necessarily claim
        // their own) can match the same titleFragment. Capture the count before claiming so Step
        // 5 below can assert it dropped by exactly one, rather than naively asserting "no match at
        // all" — which would incorrectly fail if any OTHER same-titled stray card is still sitting
        // unclaimed in the inbox.
        var matchingCountBefore = titles.Count(t =>
            t.Contains("Tom Williams",    StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Personal Details",StringComparison.OrdinalIgnoreCase));

        await inbox.ClaimAsync(taskTitle);

        // ── Step 5: After claiming, exactly one fewer matching card should remain ──
        var titlesAfterClaim = await inbox.GetTaskTitlesAsync();
        var matchingCountAfter = titlesAfterClaim.Count(t =>
            t.Contains("Tom Williams",    StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Personal Details",StringComparison.OrdinalIgnoreCase));
        Assert.Equal(matchingCountBefore - 1, matchingCountAfter);

        // ── Step 6: The claimed task appears in Laura's own Tasks tab ──────────
        // The old dashboard "My Tasks" widget (MyTasksWidget.razor) this used to check is dead
        // code — no longer rendered anywhere. Laura's own profile Tasks tab is the current,
        // role-agnostic place to find her full assigned-task list.
        await profile.GoToAsync(AcmeId, LauraId);
        await profile.OpenTasksTabAsync();
        var taskTitles = await profile.GetTaskTitlesAsync();
        Assert.Contains(taskTitles,
            t => t.Contains("Tom Williams",    StringComparison.OrdinalIgnoreCase) ||
                 t.Contains("Personal Details",StringComparison.OrdinalIgnoreCase));
    }
}
