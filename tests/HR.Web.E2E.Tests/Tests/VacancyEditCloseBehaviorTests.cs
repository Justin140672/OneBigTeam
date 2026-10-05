using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class VacancyEditCloseBehaviorTests(RecruiterPersonaFixture fixture) : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task Close_ExistingRecordWithNoChanges_NavigatesDirectlyToList()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // Create a vacancy first so we have an existing, unmodified record to reopen. A fresh
        // Position Profile is required rather than the seeded "Senior Software Engineer" — that
        // profile already has a permanently-open vacancy in seed data (see
        // PositionProfileTestHelpers' remarks), which the "one live vacancy per position profile"
        // rule would otherwise reject a second vacancy against.
        var profileTitle = await PositionProfileTestHelpers.CreateUniquePositionProfileAsync(
            _page, _fixture.WebBaseUrl, AcmeId, login, LauraEmail, MarcusEmail);

        var vacancyTitle = $"E2E Close {Guid.NewGuid().ToString("N")[..8]}";
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SelectEmploymentTypeAsync("Permanent");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.GoToAsync(AcmeId);
        Assert.True(await vacancyList.HasVacancyAsync(vacancyTitle));

        await vacancyList.ClickVacancyAsync(vacancyTitle);
        await vacancyDetail.CloseAndWaitForListAsync();

        Assert.EndsWith("/vacancies", _page.Url);
    }

    [Fact]
    public async Task Close_NewRecordWithUnsavedChanges_ShowsConfirmDialog()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyDetail.GoToNewAsync(AcmeId);
        await vacancyDetail.FillTitleAsync("Unsaved Vacancy Title");

        await vacancyDetail.ClickCloseAsync();

        Assert.True(await vacancyDetail.IsUnsavedChangesDialogVisibleAsync(),
            "Expected the unsaved-changes confirmation dialog to appear when closing with edits pending");
        Assert.Contains("/vacancies/new", _page.Url);
    }

    [Fact]
    public async Task Close_DiscardChanges_NavigatesAwayWithoutSaving()
    {
        var vacancyTitle = $"E2E Discard {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyDetail.GoToNewAsync(AcmeId);
        await vacancyDetail.FillTitleAsync(vacancyTitle);

        await vacancyDetail.ClickCloseAsync();
        Assert.True(await vacancyDetail.IsUnsavedChangesDialogVisibleAsync());

        await vacancyDetail.ConfirmDiscardChangesAsync();

        Assert.EndsWith("/vacancies", _page.Url);

        await vacancyList.GoToAsync(AcmeId);
        Assert.False(await vacancyList.HasVacancyAsync(vacancyTitle),
            "Discarding changes should not have created the vacancy");
    }

    [Fact]
    public async Task Close_SaveFromUnsavedChangesDialog_SavesAndNavigatesToList()
    {
        var vacancyTitle = $"E2E SaveOnClose {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var profileTitle = await PositionProfileTestHelpers.CreateUniquePositionProfileAsync(
            _page, _fixture.WebBaseUrl, AcmeId, login, LauraEmail, MarcusEmail);

        await vacancyDetail.GoToNewAsync(AcmeId);
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SelectEmploymentTypeAsync("Permanent");

        await vacancyDetail.ClickCloseAsync();
        Assert.True(await vacancyDetail.IsUnsavedChangesDialogVisibleAsync());

        await vacancyDetail.ConfirmSaveFromUnsavedChangesDialogAsync();

        Assert.EndsWith("/vacancies", _page.Url);
        Assert.True(await vacancyList.HasVacancyAsync(vacancyTitle),
            "Choosing Save from the unsaved-changes dialog should have created the vacancy");
    }

    [Fact]
    public async Task Close_CancelUnsavedChangesDialog_StaysOnPageWithFieldIntact()
    {
        var vacancyTitle = $"E2E CancelClose {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyDetail.GoToNewAsync(AcmeId);
        await vacancyDetail.FillTitleAsync(vacancyTitle);

        await vacancyDetail.ClickCloseAsync();
        Assert.True(await vacancyDetail.IsUnsavedChangesDialogVisibleAsync());

        await vacancyDetail.CancelUnsavedChangesDialogAsync();

        Assert.Contains("/vacancies/new", _page.Url);
        Assert.Equal(vacancyTitle, await vacancyDetail.GetTitleAsync());
    }
}
