using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "Internal Vacancies" self-service feature:
/// - A Recruiter can tick "Advertise this vacancy to employees" on an Open vacancy and save.
/// - A plain employee then sees the "Internal Vacancies" quick action on My Profile, can open the
///   list, and sees the advertised vacancy as a card whose read-only detail dialog has no
///   Apply/Save action.
/// - The list page itself is NOT recruiter-gated (unlike /vacancies) — a plain employee reaches it
///   directly without an access-denied redirect.
/// - An Open vacancy that is NOT advertised internally does not appear in the employee list.
///
/// Dependency: these tests rely on the seeded Acme "Senior Software Engineer" vacancy
/// (RecruitmentModule.SeedRecruitmentAsync) being Open. It is NOT advertised internally in seed
/// data, so every test that needs the populated list first flips that flag on via the Recruiter UI
/// (idempotent — the checkbox just stays checked once saved).
///
/// Joins CrossUserVacancyTestBase's serialization group: the positive-path tests mutate the shared
/// seeded vacancy, and other recruitment tests in that group read the same shared data.
/// </summary>
public sealed class InternalVacanciesTests(CrossUserFixture fixture) : CrossUserVacancyTestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string MarcusEmail = "marcus.diallo@acme.example"; // Recruiter
    private const string LauraEmail = "laura.bennett@acme.example";   // HR Administrator
    private const string TomEmail = "tom.williams@acme.example";      // plain employee

    private const string SeededVacancyTitle = "Senior Software Engineer";
    private const string SeededVacancyDescriptionFragment = "Own delivery of core platform services";

    /// <summary>
    /// Ensures the seeded "Senior Software Engineer" vacancy is Open + advertised internally,
    /// driving it entirely through the Recruiter UI. Idempotent — safe to call from every test.
    /// </summary>
    private async Task EnsureSeededVacancyAdvertisedInternallyAsync(LoginPage login)
    {
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(SeededVacancyTitle);

        if (await vacancyDetail.IsAdvertiseInternallyCheckedAsync())
            return;

        await vacancyDetail.SetAdvertiseInternallyAsync(true);
        await vacancyDetail.SaveExistingVacancyAsync();
    }

    [Fact]
    public async Task Recruiter_CanAdvertiseOpenVacancyInternally_AndSave()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(SeededVacancyTitle);

        await vacancyDetail.SetAdvertiseInternallyAsync(true);
        await vacancyDetail.SaveExistingVacancyAsync();

        Assert.False(await vacancyDetail.HasErrorAsync(),
            "Did not expect an error after saving a vacancy with 'advertise internally' ticked");

        // Reopen and confirm the flag persisted.
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(SeededVacancyTitle);
        Assert.True(await vacancyDetail.IsAdvertiseInternallyCheckedAsync(),
            "Expected 'advertise this vacancy to employees' to stay checked after save + reload");
    }

    [Fact]
    public async Task PlainEmployee_SeesInternalVacanciesQuickAction_AndAdvertisedVacancyAsCard()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await EnsureSeededVacancyAdvertisedInternallyAsync(login);

        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var overview = new OverviewTab(_page);
        var internalVacancies = new InternalVacanciesPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        if (!await overview.IsVisibleAsync())
            await profile.OpenOverviewTabAsync();
        await overview.WaitForLoadAsync();

        var quickAction = _page.Locator("[data-testid='internal-vacancies-action']");
        Assert.True(await quickAction.WaitUntilVisibleAsync(),
            "Expected the 'Internal Vacancies' quick action on My Profile when the company has an internally-advertised open vacancy");

        await quickAction.ClickAsync();
        await internalVacancies.WaitForInteractiveAsync();

        Assert.Equal("Internal Vacancies", await internalVacancies.GetHeadingAsync());
        Assert.True(await internalVacancies.HasCardAsync(SeededVacancyTitle),
            $"Expected a card for the advertised vacancy '{SeededVacancyTitle}' in the employee's internal vacancy list");
    }

    [Fact]
    public async Task PlainEmployee_OpeningCard_ShowsReadOnlyDetailDialog_WithNoApplyOrSaveButton()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await EnsureSeededVacancyAdvertisedInternallyAsync(login);

        var internalVacancies = new InternalVacanciesPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await internalVacancies.GoToAsync(AcmeId);
        await internalVacancies.OpenCardAsync(SeededVacancyTitle);

        Assert.True(await internalVacancies.IsDetailVisibleAsync(),
            "Expected the read-only internal-vacancy-detail dialog to open");
        Assert.Contains(SeededVacancyTitle, await internalVacancies.GetDetailTitleAsync() ?? "");
        Assert.Contains(SeededVacancyDescriptionFragment, await internalVacancies.GetDetailDescriptionAsync() ?? "");

        Assert.Equal(0, await internalVacancies.DetailButtonCountAsync("Apply"));
        Assert.Equal(0, await internalVacancies.DetailButtonCountAsync("Save"));
    }

    [Fact]
    public async Task PlainEmployee_NavigatingDirectlyToInternalVacancies_IsNotRedirectedToAccessDenied()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var internalVacancies = new InternalVacanciesPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await internalVacancies.GoToAsync(AcmeId);

        Assert.Contains("/internal-vacancies", _page.Url);
        Assert.DoesNotContain("/access-denied", _page.Url);
        Assert.Equal("Internal Vacancies", await internalVacancies.GetHeadingAsync());
    }

    [Fact]
    public async Task OpenVacancyNotAdvertisedInternally_DoesNotAppearInEmployeeList()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        // Ensure the list is populated (seeded vacancy advertised) so this is a real "present vs
        // absent" contrast rather than just an empty list.
        await EnsureSeededVacancyAdvertisedInternallyAsync(login);

        var unique = Guid.NewGuid().ToString("N")[..8];
        var vacancyTitle = $"E2E NotInternal {unique}";

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var profileTitle = await PositionProfileTestHelpers.CreateUniquePositionProfileAsync(
            _page, _fixture.WebBaseUrl, AcmeId, login, LauraEmail, MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        // Publish it so it's Open — but leave "advertise internally" unchecked.
        await vacancyList.ClickVacancyAsync(vacancyTitle);
        Assert.False(await vacancyDetail.IsAdvertiseInternallyCheckedAsync(),
            "Sanity: a freshly-created vacancy should not be advertised internally by default");
        await vacancyDetail.PublishVacancyAsync();

        // Now view the internal list as a plain employee.
        var internalVacancies = new InternalVacanciesPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);
        await internalVacancies.GoToAsync(AcmeId);

        Assert.True(await internalVacancies.HasCardAsync(SeededVacancyTitle),
            "Expected the advertised seeded vacancy to be present (control)");

        await internalVacancies.SearchAsync(vacancyTitle);
        Assert.Equal(0, await internalVacancies.CardCountAsync());
    }
}
