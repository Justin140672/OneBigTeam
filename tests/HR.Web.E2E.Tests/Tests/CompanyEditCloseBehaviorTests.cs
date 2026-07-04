using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Direct coverage of the Close / unsaved-changes prompt on the Company edit page.
/// CompanyEdit is a non-trivial host for this shared EditPageBase behavior: it has no
/// dedicated "list" page (Close navigates to the dashboard instead), its own Save button
/// intentionally stays on the page showing an inline success banner (unlike most edit pages),
/// and its <c>HasUnsavedChanges</c> override folds in the Settings tab's independently-saved
/// model — so an edit made purely on the Settings tab must still trigger the Close prompt, and
/// choosing "Save" from that prompt must navigate away even though the page's own Save button
/// doesn't.
/// </summary>
[Collection("E2E")]
public sealed class CompanyEditCloseBehaviorTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task Close_WithNoChanges_NavigatesDirectlyToDashboard()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await companyEdit.GoToAsync(AcmeId);

        await companyEdit.CloseAndWaitForDashboardAsync(_fixture.WebBaseUrl);

        Assert.Equal($"{_fixture.WebBaseUrl}/", _page.Url);
    }

    [Fact]
    public async Task Close_ProfileNameEditedWithoutSaving_ShowsConfirmDialog()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenProfileTabAsync();

        var originalName = await companyEdit.GetCompanyNameInputValueAsync();
        await companyEdit.FillCompanyNameInputAsync($"{originalName} (edited)");

        await companyEdit.ClickCloseAsync();

        Assert.True(await companyEdit.IsUnsavedChangesDialogVisibleAsync(),
            "Expected the unsaved-changes dialog when closing with an edited Profile name");
    }

    [Fact]
    public async Task Close_SettingsTabEditOnly_StillShowsConfirmDialog()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        // Edit a field that lives entirely on the Settings tab's own model — this is what
        // exercises CompanyEdit's HasUnsavedChanges override.
        var initialExcludeLeave = await companyEdit.IsExcludePublicHolidaysFromLeaveCheckedAsync();
        await companyEdit.SetExcludePublicHolidaysFromLeaveAsync(!initialExcludeLeave);

        await companyEdit.ClickCloseAsync();

        Assert.True(await companyEdit.IsUnsavedChangesDialogVisibleAsync(),
            "Expected the unsaved-changes dialog to appear for an edit made only on the Settings tab");
    }

    [Fact]
    public async Task Close_DiscardSettingsChange_NavigatesAwayWithoutSaving()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        var initialExcludeLeave = await companyEdit.IsExcludePublicHolidaysFromLeaveCheckedAsync();
        await companyEdit.SetExcludePublicHolidaysFromLeaveAsync(!initialExcludeLeave);

        await companyEdit.ClickCloseAsync();
        Assert.True(await companyEdit.IsUnsavedChangesDialogVisibleAsync());

        await companyEdit.ConfirmDiscardChangesAsync(_fixture.WebBaseUrl);
        Assert.Equal($"{_fixture.WebBaseUrl}/", _page.Url);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();
        Assert.Equal(initialExcludeLeave, await companyEdit.IsExcludePublicHolidaysFromLeaveCheckedAsync());
    }

    [Fact]
    public async Task Close_SaveFromUnsavedChangesDialog_PersistsAndNavigatesAway()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        var initialExcludeLeave = await companyEdit.IsExcludePublicHolidaysFromLeaveCheckedAsync();
        var desiredExcludeLeave = !initialExcludeLeave;
        await companyEdit.SetExcludePublicHolidaysFromLeaveAsync(desiredExcludeLeave);

        await companyEdit.ClickCloseAsync();
        Assert.True(await companyEdit.IsUnsavedChangesDialogVisibleAsync());

        // Choosing Save from the prompt should persist the change AND navigate away — unlike
        // the page's own Save button, which stays put and shows an inline success banner.
        await companyEdit.ConfirmSaveFromUnsavedChangesDialogAsync(_fixture.WebBaseUrl);
        Assert.Equal($"{_fixture.WebBaseUrl}/", _page.Url);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();
        Assert.Equal(desiredExcludeLeave, await companyEdit.IsExcludePublicHolidaysFromLeaveCheckedAsync());
    }

    [Fact]
    public async Task Close_CancelUnsavedChangesDialog_StaysOnPageWithFieldIntact()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenProfileTabAsync();

        var originalName = await companyEdit.GetCompanyNameInputValueAsync();
        var editedName = $"{originalName} (edited)";
        await companyEdit.FillCompanyNameInputAsync(editedName);

        await companyEdit.ClickCloseAsync();
        Assert.True(await companyEdit.IsUnsavedChangesDialogVisibleAsync());

        await companyEdit.CancelUnsavedChangesDialogAsync();

        Assert.Contains("/edit", _page.Url);
        await companyEdit.OpenProfileTabAsync();
        Assert.Equal(editedName, await companyEdit.GetCompanyNameInputValueAsync());
    }
}
