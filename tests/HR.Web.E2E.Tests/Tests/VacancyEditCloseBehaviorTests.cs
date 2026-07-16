using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Close button and "unsaved changes" confirmation prompt that <c>EditPageBase</c>
/// provides to every edit page (see EditPageBase.cs / UnsavedChangesDialog.razor). Exercised
/// via the Vacancy detail page as a representative host — the behavior under test lives in
/// the shared base class, not in VacancyDetail itself.
///
/// Uses Marcus Diallo (Recruiter role) rather than Laura Bennett (HR Administrator) —
/// recruitment:manage (vacancy creation) is Recruiter-only (see IdentityModule.AddRolePolicies);
/// an HR Administrator does not automatically get recruitment access.
/// </summary>
[Collection("E2E")]
public sealed class VacancyEditCloseBehaviorTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task Close_ExistingRecordWithNoChanges_NavigatesDirectlyToList()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // Create a vacancy first so we have an existing, unmodified record to reopen.
        var vacancyTitle = $"E2E Close {Guid.NewGuid().ToString("N")[..8]}";
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.FillLocationAsync("Remote");
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.GoToAsync(AcmeId);
        Assert.True(await vacancyList.HasVacancyAsync(vacancyTitle));

        // Reopening it and clicking Close with no edits should navigate straight back to the
        // list — no "unsaved changes" prompt should appear (the wait inside CloseAndWaitForListAsync
        // would time out if one blocked navigation).
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

        await vacancyDetail.GoToNewAsync(AcmeId);
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.FillLocationAsync("Remote");
        await vacancyDetail.SelectHiringManagerAsync("James");

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

        // Cancelling the prompt should just dismiss it — the user stays on the form with
        // their edits untouched, free to keep editing or click Close again.
        Assert.Contains("/vacancies/new", _page.Url);
        Assert.Equal(vacancyTitle, await vacancyDetail.GetTitleAsync());
    }
}
