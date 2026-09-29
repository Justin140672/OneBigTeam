using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class SupportAndFeedbackTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string TomEmail   = "tom.williams@acme.example";

    [Fact]
    public async Task HelpFeedbackPage_LoadsForStaffPersona()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var help  = new HelpFeedbackPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await help.GoToAsync(AcmeId);

        Assert.True(await _page.GetByPlaceholder("Short summary").IsVisibleAsync(),
            "Expected the Title field to render on the Help & Feedback page");

        await help.WaitForSubmissionsLoadedAsync();
    }

    [Fact]
    public async Task CreateSupportRequest_AppearsInSubmissionsAndDetailPage()
    {
        var title = $"E2E Support Request {Guid.NewGuid().ToString("N")[..8]}";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var help  = new HelpFeedbackPage(_page, _fixture.WebBaseUrl);
        var detail = new SupportRequestDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await help.GoToAsync(AcmeId);

        await help.SelectTypeAsync("Report a Problem");
        await help.FillTitleAsync(title);
        await help.FillDescriptionAsync("Created by E2E test — please ignore.");
        await help.SelectPriorityAsync("Medium");
        Assert.True(await help.IsIncludeDiagnosticsCheckedAsync(),
            "Expected 'Include diagnostics' to default to checked");

        await help.SubmitAsync();

        Assert.Equal(title, await detail.GetTitleAsync());

        await help.GoToAsync(AcmeId);
        Assert.True(await help.HasSubmissionAsync(title),
            $"Expected the new support request '{title}' to appear in My Submissions");
    }

    // NOTE: This used to drive a per-row status dropdown in the staff queue
    // (SupportRequestQueuePage.ChangeStatusAsync). Per SupportRequestQueue.razor's own banner
    // ("Ticket status can only be changed by support staff in the Admin app.") and its
    // IsAddDisabled/Status-column comments, that capability was deliberately removed — status is
    // now a plain read-only, humanized text cell (EnumDisplay.Humanize) with no dropdown at all.
    // Updated to assert the current, correct behavior: a staff persona can see a newly-submitted
    // request's status in the queue, but there is no control to change it from here.
    [Fact]
    public async Task StaffPersona_SeesRequestStatus_InQueue_ButCannotChangeIt()
    {
        var title = $"E2E Queue Status {Guid.NewGuid().ToString("N")[..8]}";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var help  = new HelpFeedbackPage(_page, _fixture.WebBaseUrl);
        var queue = new SupportRequestQueuePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await help.GoToAsync(AcmeId);
        await help.SelectTypeAsync("Ask a Question");
        await help.FillTitleAsync(title);
        await help.FillDescriptionAsync("Created by E2E test — please ignore.");
        await help.SelectPriorityAsync("Low");
        await help.SubmitAsync();

        await queue.GoToAsync(AcmeId);
        Assert.True(await queue.HasRequestAsync(title),
            $"Expected the submitted request '{title}' to appear in the staff queue");

        Assert.True(await queue.HasStatusTextAsync(title, "Submitted"),
            $"Expected the queue row for '{title}' to show its status ('Submitted') as plain text");
        Assert.False(await queue.HasStatusDropdownAsync(title),
            $"Did not expect an editable status dropdown in the staff queue row for '{title}' — " +
            "status can only be changed by support staff in the Admin app");
    }

    [Fact]
    public async Task SupportRequestDetailPage_StaffCanPostReply()
    {
        var title = $"E2E Reply {Guid.NewGuid().ToString("N")[..8]}";
        var replyText = $"Thanks for reporting this — reply {Guid.NewGuid().ToString("N")[..6]}";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var help  = new HelpFeedbackPage(_page, _fixture.WebBaseUrl);
        var detail = new SupportRequestDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await help.GoToAsync(AcmeId);
        await help.SelectTypeAsync("Request a Feature");
        await help.FillTitleAsync(title);
        await help.FillDescriptionAsync("Created by E2E test — please ignore.");
        await help.SelectPriorityAsync("High");
        await help.SubmitAsync();

        await detail.FillReplyAsync(replyText);
        await detail.SendReplyAsync();

        Assert.True(await detail.HasThreadEntryAsync(replyText),
            $"Expected the reply '{replyText}' to appear in the conversation thread");
    }

    [Fact]
    public async Task PlainEmployee_IsRedirectedAway_FromHelpFeedbackPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/support");
        await WaitForUrlToStopContainingAsync("/support");

        var finalUrl = _page.Url;
        Assert.False(finalUrl.TrimEnd('/').EndsWith("/support"),
            $"Expected a plain employee to be redirected away from the Help & Feedback page, but ended up at: {finalUrl}");
    }

    [Fact]
    public async Task PlainEmployee_IsRedirectedAway_FromSupportRequestDetailPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var help  = new HelpFeedbackPage(_page, _fixture.WebBaseUrl);

        var title = $"E2E Detail Guard {Guid.NewGuid().ToString("N")[..8]}";

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await help.GoToAsync(AcmeId);
        await help.SelectTypeAsync("Ask a Question");
        await help.FillTitleAsync(title);
        await help.FillDescriptionAsync("Created by E2E test — please ignore.");
        await help.SelectPriorityAsync("Low");
        await help.SubmitAsync();

        var detailUrl = _page.Url;

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync(detailUrl);
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var finalUrl = _page.Url;
        Assert.False(finalUrl == detailUrl,
            $"Expected a plain employee to be redirected away from the support request detail page, but ended up at: {finalUrl}");
    }

    [Fact]
    public async Task PlainEmployee_IsRedirectedAway_FromSupportQueuePage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/support/admin/queue");
        await WaitForUrlToStopContainingAsync("/support/admin/queue");

        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/support/admin/queue"),
            $"Expected a plain employee to be redirected away from the support queue page, but ended up at: {finalUrl}");
    }

}
