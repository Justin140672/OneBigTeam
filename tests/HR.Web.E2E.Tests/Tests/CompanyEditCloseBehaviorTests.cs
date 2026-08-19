using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Direct coverage of the Close / unsaved-changes prompt on the Company edit page.
/// CompanyEdit is a non-trivial host for this shared EditPageBase behavior: it has no
/// dedicated "list" page (Close navigates to the dashboard instead), and its own Save button
/// intentionally stays on the page showing an inline success banner (unlike most edit pages).
///
/// This used to also cover an edit made purely on the (now-removed) Settings tab, which had its
/// own independently-saved model outside EditPageBase's tracked <c>Model</c> and needed a
/// <c>HasUnsavedChanges</c> override to fold in — that override no longer exists now that
/// Settings is gone. Company name and addresses both live on the same Profile tab/EditContext
/// now, so the base <c>HasUnsavedChanges</c> (a snapshot diff of <c>Model</c> — see
/// EditPageBase&lt;TModel&gt;) already covers both without any page-specific override.
/// </summary>
public sealed class CompanyEditCloseBehaviorTests(PriyaShahPersonaFixture fixture) : RoleE2ETestBase<PriyaShahPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // CompanyEdit's edit mode (LoadAsync) gates on Session.CanManageCompany, which the
    // company:manage policy restricts to CompanyAdministrator — HrAdministrator no longer
    // qualifies, so these tests need a CompanyAdministrator-only persona.
    private const string CompanyAdminEmail = "priya.shah@acme.example";

    [Fact]
    public async Task Close_WithNoChanges_NavigatesDirectlyToDashboard()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);

        await companyEdit.CloseAndWaitForDashboardAsync(_fixture.WebBaseUrl, AcmeId);

        Assert.Equal($"{_fixture.WebBaseUrl}/companies/{AcmeId}/edit", _page.Url);
    }

    [Fact]
    public async Task Close_ProfileNameEditedWithoutSaving_ShowsConfirmDialog()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenProfileTabAsync();

        var originalName = await companyEdit.GetCompanyNameInputValueAsync();
        await companyEdit.FillCompanyNameInputAsync($"{originalName} (edited)");

        await companyEdit.ClickCloseAsync();

        Assert.True(await companyEdit.IsUnsavedChangesDialogVisibleAsync(),
            "Expected the unsaved-changes dialog when closing with an edited Profile name");
    }

    /// <summary>
    /// Choosing "Save" from the unsaved-changes prompt always navigates away on success — unlike
    /// the page's own Save button, which stays put and shows an inline success banner. Exercised
    /// via the Profile tab's Company Name field now that Settings (which used to carry this
    /// coverage via its own independently-saved model) is gone.
    /// </summary>
    [Fact]
    public async Task Close_SaveFromUnsavedChangesDialog_PersistsAndNavigatesAway()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenProfileTabAsync();

        var originalName = await companyEdit.GetCompanyNameInputValueAsync();
        var desiredName = $"{originalName} (edited)";
        await companyEdit.FillCompanyNameInputAsync(desiredName);

        await companyEdit.ClickCloseAsync();
        Assert.True(await companyEdit.IsUnsavedChangesDialogVisibleAsync());

        await companyEdit.ConfirmSaveFromUnsavedChangesDialogAsync(_fixture.WebBaseUrl, AcmeId);
        Assert.Equal($"{_fixture.WebBaseUrl}/companies/{AcmeId}/edit", _page.Url);

        try
        {
            await companyEdit.GoToAsync(AcmeId);
            await companyEdit.OpenProfileTabAsync();
            Assert.Equal(desiredName, await companyEdit.GetCompanyNameInputValueAsync());
        }
        finally
        {
            // Restore the original name so this test doesn't leak state into other tests that
            // rely on the seeded "Acme Corp" name.
            await companyEdit.FillCompanyNameInputAsync(originalName);
            await companyEdit.SaveAsync();
        }
    }

    [Fact]
    public async Task Close_CancelUnsavedChangesDialog_StaysOnPageWithFieldIntact()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

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
