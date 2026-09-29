using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class PersonalDetailsTabTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail = "tom.williams@acme.example";

    [Fact]
    public async Task PersonalDetailsTab_ShowsEmployeeName()
    {
        var login          = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile        = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var personalDetails = new PersonalDetailsTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenPersonalDetailsTabAsync();
        await personalDetails.WaitForLoadAsync();

        Assert.True(await personalDetails.IsVisibleAsync(),
            "Expected the Personal Details card to render");

        var firstName = await personalDetails.GetDetailAsync("First Name");
        Assert.False(string.IsNullOrWhiteSpace(firstName),
            "Expected First Name to be displayed");
        Assert.Contains("Tom", firstName, StringComparison.OrdinalIgnoreCase);

        var lastName = await personalDetails.GetDetailAsync("Last Name");
        Assert.False(string.IsNullOrWhiteSpace(lastName),
            "Expected Last Name to be displayed");
        Assert.Contains("Williams", lastName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PersonalDetailsTab_RequestChange_WithNotes_ShowsSuccessBanner()
    {
        var notes = $"E2E-PD-{Guid.NewGuid():N}: Please update my preferred name to Tommy.";

        var login           = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile         = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var personalDetails = new PersonalDetailsTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenPersonalDetailsTabAsync();
        await personalDetails.WaitForLoadAsync();

        await personalDetails.ClickRequestChangeAsync();
        Assert.True(await personalDetails.IsDialogOpenAsync());

        await personalDetails.FillChangeRequestNotesAsync(notes);
        await personalDetails.SubmitChangeRequestAsync();

        Assert.False(await personalDetails.IsDialogOpenAsync(),
            "Dialog should be closed after successful submission");
        Assert.True(await personalDetails.IsSuccessBannerVisibleAsync(),
            "Expected a success banner after submitting a change request");
    }

    [Fact]
    public async Task PersonalDetailsTab_RequestChange_Cancel_ClosesDialog()
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
        Assert.True(await personalDetails.IsDialogOpenAsync());

        await personalDetails.CancelChangeRequestAsync();

        await _page.WaitForSelectorAsync(".e-dialog",
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

        Assert.False(await personalDetails.IsDialogOpenAsync(),
            "Dialog should be closed after cancelling");

        Assert.False(await personalDetails.IsSuccessBannerVisibleAsync(),
            "No success banner should appear when the request was cancelled");
    }
}
