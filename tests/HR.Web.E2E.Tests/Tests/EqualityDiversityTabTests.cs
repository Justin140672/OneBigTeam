using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the self-service "Equality &amp; Diversity" tab on the My Profile page
/// (src/HR.Web/Components/Pages/Employees/MyProfileEqualityDiversityTab.razor):
/// - the tab loads and shows the "voluntary" explanatory text
/// - answers can be selected and saved, showing a success banner
/// - saved answers persist across re-opening the tab
/// - "Prefer not to say" can be chosen and saved
/// - "Clear my answers" (native confirm) resets the questionnaire
/// - partial completion saves successfully
///
/// Follows EmergencyContactsTabTests / PersonalDetailsTabTests: EmployeePersonaFixture, explicit
/// login as Tom Williams, MyProfilePage for navigation.
/// </summary>
public sealed class EqualityDiversityTabTests(EmployeePersonaFixture fixture)
    : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail = "tom.williams@acme.example";

    private async Task<(MyProfilePage profile, EqualityDiversityTab ed)> OpenTabAsync()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var ed      = new EqualityDiversityTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenEqualityDiversityTabAsync();
        await ed.WaitForLoadAsync();

        return (profile, ed);
    }

    [Fact]
    public async Task EqualityDiversityTab_Loads_WithVoluntaryExplanatoryText()
    {
        var (_, ed) = await OpenTabAsync();

        Assert.True(await ed.IsSectionVisibleAsync(),
            "Expected the equality & diversity section to render for the employee's own profile");

        var intro = await ed.GetIntroTextAsync();
        Assert.Contains("voluntary", intro, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Prefer not to say", intro, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("incomplete", intro, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EqualityDiversityTab_SelectAnswersAndSave_ShowsSuccessBanner_AndPersists()
    {
        var (profile, ed) = await OpenTabAsync();

        await ed.SelectAsync(EqualityDiversityTab.GenderField, "Man");
        await ed.SelectAsync(EqualityDiversityTab.MaritalField, "No");
        await ed.SelectAsync(EqualityDiversityTab.EthnicGroupField, "White");
        await ed.SelectAsync(EqualityDiversityTab.DisabilityField, "No");
        await ed.SelectAsync(EqualityDiversityTab.OrientationField, "Heterosexual or straight");
        await ed.SelectAsync(EqualityDiversityTab.ReligionField, "No religion");

        await ed.SaveAsync();

        Assert.True(await ed.IsSuccessBannerVisibleAsync(),
            "Expected a success banner after saving equality & diversity answers");

        // Re-open the tab (leave then back) and confirm the answers persisted.
        await profile.OpenEmergencyContactsTabAsync();
        await profile.OpenEqualityDiversityTabAsync();
        await ed.WaitForLoadAsync();

        Assert.Equal("Man", await ed.GetSelectedValueAsync(EqualityDiversityTab.GenderField));
        Assert.Equal("White", await ed.GetSelectedValueAsync(EqualityDiversityTab.EthnicGroupField));
        Assert.Equal("No religion", await ed.GetSelectedValueAsync(EqualityDiversityTab.ReligionField));
    }

    [Fact]
    public async Task EqualityDiversityTab_ChangeAnswerToPreferNotToSay_Saves()
    {
        var (profile, ed) = await OpenTabAsync();

        await ed.SelectAsync(EqualityDiversityTab.EthnicGroupField, "White");
        await ed.SaveAsync();

        await ed.SelectAsync(EqualityDiversityTab.EthnicGroupField, "Prefer not to say");
        await ed.SaveAsync();

        Assert.True(await ed.IsSuccessBannerVisibleAsync(),
            "Expected a success banner after changing an answer to 'Prefer not to say'");

        await profile.OpenEmergencyContactsTabAsync();
        await profile.OpenEqualityDiversityTabAsync();
        await ed.WaitForLoadAsync();

        Assert.Equal("Prefer not to say",
            await ed.GetSelectedValueAsync(EqualityDiversityTab.EthnicGroupField));
    }

    [Fact]
    public async Task EqualityDiversityTab_ClearMyAnswers_ResetsQuestionnaire()
    {
        var (profile, ed) = await OpenTabAsync();

        // Accept the native window.confirm raised by "Clear my answers".
        ed.AcceptConfirmDialogs();

        await ed.SelectAsync(EqualityDiversityTab.GenderField, "Woman");
        await ed.SelectAsync(EqualityDiversityTab.ReligionField, "Christian");
        await ed.SaveAsync();

        await ed.ClearAnswersAsync();

        Assert.Contains("cleared", await ed.GetSuccessBannerTextAsync(),
            StringComparison.OrdinalIgnoreCase);

        // Fields fall back to the "Not answered" placeholder after a clear.
        Assert.Equal("Not answered", await ed.GetSelectedValueAsync(EqualityDiversityTab.GenderField));
        Assert.Equal("Not answered", await ed.GetSelectedValueAsync(EqualityDiversityTab.ReligionField));

        // And the clear persists on re-open.
        await profile.OpenEmergencyContactsTabAsync();
        await profile.OpenEqualityDiversityTabAsync();
        await ed.WaitForLoadAsync();

        Assert.Equal("Not answered", await ed.GetSelectedValueAsync(EqualityDiversityTab.GenderField));
    }

    [Fact]
    public async Task EqualityDiversityTab_PartialCompletion_SavesSuccessfully()
    {
        var (profile, ed) = await OpenTabAsync();

        // Only two of the six questions answered.
        await ed.SelectAsync(EqualityDiversityTab.DisabilityField, "Yes");
        await ed.SelectAsync(EqualityDiversityTab.OrientationField, "Bisexual");

        await ed.SaveAsync();

        Assert.True(await ed.IsSuccessBannerVisibleAsync(),
            "Expected a partially-completed questionnaire to save successfully");

        await profile.OpenEmergencyContactsTabAsync();
        await profile.OpenEqualityDiversityTabAsync();
        await ed.WaitForLoadAsync();

        Assert.Equal("Yes", await ed.GetSelectedValueAsync(EqualityDiversityTab.DisabilityField));
        Assert.Equal("Bisexual", await ed.GetSelectedValueAsync(EqualityDiversityTab.OrientationField));
        Assert.Equal("Not answered", await ed.GetSelectedValueAsync(EqualityDiversityTab.GenderField));
    }
}
