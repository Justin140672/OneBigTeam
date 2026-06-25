using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the personal details change request flow:
/// 1. Employee submits a change request from the Personal Details tab.
/// 2. A success banner confirms the submission.
/// 3. An HR task is created and visible in the HR Inbox.
/// </summary>
[Collection("E2E")]
public sealed class PersonalDetailsChangeRequestTests : IAsyncLifetime
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail   = "tom.williams@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    private readonly AppFixture _fixture;
    private IBrowserContext _context = null!;
    private IPage           _page    = null!;

    public PersonalDetailsChangeRequestTests(AppFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _context = await _fixture.Browser.NewContextAsync();
        _page    = await _context.NewPageAsync();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task SubmittingChangeRequest_ShowsSuccessBanner_AndCreatesHrTask()
    {
        var notes = $"E2E-PDC-{Guid.NewGuid():N}: Please update my preferred name to Alex.";

        var login          = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile        = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var personalDetails = new PersonalDetailsTab(_page);
        var inbox          = new HrInboxPage(_page, _fixture.WebBaseUrl);
        var dash           = new DashboardPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Tom ──────────────────────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        // ── Step 2: Navigate to Tom's self-service profile ────────────────────
        await profile.GoToAsync(AcmeId, TomId);

        // ── Step 3: Open Personal Details tab ─────────────────────────────────
        await profile.OpenPersonalDetailsTabAsync();
        await personalDetails.WaitForLoadAsync();

        // ── Step 4: Verify the card is visible (not a permission error) ───────
        Assert.True(await personalDetails.IsVisibleAsync(),
            "Expected the Personal Details card to be rendered for Tom's own profile");

        // ── Step 5: Open the Request Change dialog ────────────────────────────
        await personalDetails.ClickRequestChangeAsync();
        Assert.True(await personalDetails.IsDialogOpenAsync(),
            "Expected the 'Request Change to Personal Details' dialog to open");

        // ── Step 6: Fill in the change request notes ──────────────────────────
        await personalDetails.FillChangeRequestNotesAsync(notes);

        // ── Step 7: Submit the request ────────────────────────────────────────
        await personalDetails.SubmitChangeRequestAsync();

        // ── Step 8: Success banner should appear ──────────────────────────────
        Assert.True(await personalDetails.IsSuccessBannerVisibleAsync(),
            "Expected a success banner after submitting the change request");

        // ── Step 9: Switch to Laura and verify the task appeared in HR Inbox ──
        await login.SwitchAccountAsync(LauraEmail);

        // The change request creates a task. It should appear in Laura's dashboard
        // or in the HR Inbox (it is routed as an unassigned HR task).
        await dash.GoToAsync();
        var taskTitles = await dash.GetTaskTitlesAsync();

        // The task title should reference either "personal details" or Tom's name.
        var taskVisible = taskTitles.Any(t =>
            t.Contains("Tom Williams",       StringComparison.OrdinalIgnoreCase) ||
            t.Contains("personal details",   StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Personal Details",   StringComparison.OrdinalIgnoreCase));

        if (!taskVisible)
        {
            // Fall back to checking the HR Inbox for unassigned tasks.
            await inbox.GoToAsync(AcmeId);
            var inboxTitles = await inbox.GetTaskTitlesAsync();
            taskVisible = inboxTitles.Any(t =>
                t.Contains("Tom Williams",     StringComparison.OrdinalIgnoreCase) ||
                t.Contains("personal details", StringComparison.OrdinalIgnoreCase));
        }

        Assert.True(taskVisible,
            "Expected a personal details change-request task to appear in Laura's dashboard or HR Inbox");
    }

    [Fact]
    public async Task SubmitChangeRequest_WithEmptyNotes_ShowsValidationError()
    {
        var login           = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile         = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var personalDetails = new PersonalDetailsTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenPersonalDetailsTabAsync();
        await personalDetails.WaitForLoadAsync();

        // Open dialog and immediately try to submit without filling notes.
        await personalDetails.ClickRequestChangeAsync();
        // Use ClickSubmitRequestAsync (not SubmitChangeRequestAsync) because the dialog stays
        // open on validation failure — SubmitChangeRequestAsync would time out waiting for it to close.
        await personalDetails.ClickSubmitRequestAsync();

        // Dialog should still be open with a validation error.
        Assert.True(await personalDetails.IsDialogOpenAsync(),
            "Dialog should remain open when submitted with empty notes");

        Assert.True(await personalDetails.HasValidationErrorAsync(),
            "Expected a validation error for empty notes");
    }
}
