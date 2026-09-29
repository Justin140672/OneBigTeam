using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class PersonalDetailsChangeRequestTests(CrossUserFixture fixture) : RoleE2ETestBase<CrossUserFixture>(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId   = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid LauraId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    private const string TomEmail   = "tom.williams@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task SubmittingChangeRequest_ShowsSuccessBanner_AndCreatesHrTask()
    {
        var notes = $"E2E-PDC-{Guid.NewGuid():N}: Please update my preferred name to Alex.";

        var login          = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile        = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var personalDetails = new PersonalDetailsTab(_page);
        var inbox          = new HrInboxPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);

        await profile.OpenPersonalDetailsTabAsync();
        await personalDetails.WaitForLoadAsync();

        Assert.True(await personalDetails.IsVisibleAsync(),
            "Expected the Personal Details card to be rendered for Tom's own profile");

        await personalDetails.ClickRequestChangeAsync();
        Assert.True(await personalDetails.IsDialogOpenAsync(),
            "Expected the 'Request Change to Personal Details' dialog to open");

        await personalDetails.FillChangeRequestNotesAsync(notes);

        await personalDetails.SubmitChangeRequestAsync();

        Assert.True(await personalDetails.IsSuccessBannerVisibleAsync(),
            "Expected a success banner after submitting the change request");

        await login.SwitchAccountAsync(LauraEmail);

        await profile.GoToAsync(AcmeId, LauraId);
        await profile.OpenTasksTabAsync();
        var taskTitles = await profile.GetTaskTitlesAsync();

        var taskVisible = taskTitles.Any(t =>
            t.Contains("Tom Williams",       StringComparison.OrdinalIgnoreCase) ||
            t.Contains("personal details",   StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Personal Details",   StringComparison.OrdinalIgnoreCase));

        if (!taskVisible)
        {
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

        await personalDetails.ClickRequestChangeAsync();
        await personalDetails.ClickSubmitRequestAsync();

        Assert.True(await personalDetails.IsDialogOpenAsync(),
            "Dialog should remain open when submitted with empty notes");

        Assert.True(await personalDetails.HasValidationErrorAsync(),
            "Expected a validation error for empty notes");
    }
}
