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
[Collection("E2E")]
public sealed class HrInboxTests : IAsyncLifetime
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail   = "tom.williams@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    private readonly AppFixture _fixture;
    private IBrowserContext _context = null!;
    private IPage           _page    = null!;

    public HrInboxTests(AppFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _context = await _fixture.Browser.NewContextAsync();
        _page    = await _context.NewPageAsync();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task HrInbox_Shows_Unassigned_Task_And_Claim_Removes_It_From_Inbox()
    {
        var uniqueNotes = $"E2E-Inbox-{Guid.NewGuid():N}";

        var login           = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile         = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var personalDetails = new PersonalDetailsTab(_page);
        var inbox           = new HrInboxPage(_page, _fixture.WebBaseUrl);
        var dash            = new DashboardPage(_page, _fixture.WebBaseUrl);

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

        await inbox.ClaimAsync(taskTitle);

        // ── Step 5: After claiming, the card must be gone from the inbox ───────
        Assert.False(await inbox.HasTaskAsync(taskTitle),
            "Task should be removed from inbox after being claimed");

        // ── Step 6: The claimed task appears in Laura's dashboard task widget ──
        await dash.GoToAsync();
        var taskTitles = await dash.GetTaskTitlesAsync();
        Assert.Contains(taskTitles,
            t => t.Contains("Tom Williams",    StringComparison.OrdinalIgnoreCase) ||
                 t.Contains("Personal Details",StringComparison.OrdinalIgnoreCase));
    }
}
