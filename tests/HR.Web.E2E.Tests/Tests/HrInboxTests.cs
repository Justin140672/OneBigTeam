using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class HrInboxTests(CrossUserFixture fixture) : RoleE2ETestBase<CrossUserFixture>(fixture)
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

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenPersonalDetailsTabAsync();
        await personalDetails.WaitForLoadAsync();
        await personalDetails.ClickRequestChangeAsync();
        await personalDetails.FillChangeRequestNotesAsync(uniqueNotes);
        await personalDetails.SubmitChangeRequestAsync();

        await login.SwitchAccountAsync(LauraEmail);

        await inbox.GoToAsync(AcmeId, "Tom Williams");

        Assert.False(await inbox.IsEmptyAsync(),
            "HR inbox should not be empty after Tom submitted a personal-details change request");

        var titles = await inbox.GetTaskTitlesAsync();
        Assert.Contains(titles,
            t => t.Contains("Tom Williams", StringComparison.OrdinalIgnoreCase) ||
                 t.Contains("Personal Details", StringComparison.OrdinalIgnoreCase));

        var taskTitle = titles.First(t =>
            t.Contains("Tom Williams",    StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Personal Details",StringComparison.OrdinalIgnoreCase));

        var matchingCountBefore = titles.Count(t =>
            t.Contains("Tom Williams",    StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Personal Details",StringComparison.OrdinalIgnoreCase));

        await inbox.ClaimAsync(taskTitle);

        var titlesAfterClaim = await inbox.GetTaskTitlesAsync();
        var matchingCountAfter = titlesAfterClaim.Count(t =>
            t.Contains("Tom Williams",    StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Personal Details",StringComparison.OrdinalIgnoreCase));
        Assert.Equal(matchingCountBefore - 1, matchingCountAfter);

        await profile.GoToAsync(AcmeId, LauraId);
        await profile.OpenTasksTabAsync();
        var taskTitles = await profile.GetTaskTitlesAsync();
        Assert.Contains(taskTitles,
            t => t.Contains("Tom Williams",    StringComparison.OrdinalIgnoreCase) ||
                 t.Contains("Personal Details",StringComparison.OrdinalIgnoreCase));
    }
}
