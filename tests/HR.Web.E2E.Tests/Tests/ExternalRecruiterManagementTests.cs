using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies Recruiter CRUD workflows for the External Recruiter admin list/detail pages
/// (ExternalRecruiterList.razor / ExternalRecruiterDetail.razor):
/// - Create a new external recruiter and verify it appears in the list.
/// - Edit an external recruiter and verify the change persists.
/// - Deactivate then reactivate an external recruiter.
/// - The soft, non-blocking duplicate-agency-name warning surfaces on blur without blocking save.
///
/// Uses Marcus Diallo (Recruiter role) — ExternalRecruiterList/Detail both redirect away
/// non-Recruiters via Session.IsRecruiter (see ExternalRecruiterList.razor's OnBeforeLoadAsync and
/// ExternalRecruiterDetail.razor's OnLoadedAsync), mirroring VacancyManagementTests' reasoning for
/// using Marcus rather than Laura Bennett (HR Administrator).
/// </summary>
public sealed class ExternalRecruiterManagementTests(RecruiterPersonaFixture fixture) : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task NewExternalRecruiterForm_ContactNameField_IsOnItsOwnRow()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var recruiterEdit = new ExternalRecruiterDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await recruiterEdit.GoToNewAsync(AcmeId);

        Assert.True(await recruiterEdit.IsContactNameOnItsOwnRowAsync(),
            "Expected the 'Contact Name' field to render on its own full-width row");
    }

    [Fact]
    public async Task CreateExternalRecruiter_AppearsInList()
    {
        var agencyName = $"E2E Agency {Guid.NewGuid().ToString("N")[..8]}";

        var login          = new LoginPage(_page, _fixture.WebBaseUrl);
        var recruiterList  = new ExternalRecruiterListPage(_page, _fixture.WebBaseUrl);
        var recruiterEdit  = new ExternalRecruiterDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await recruiterList.GoToAsync(AcmeId);
        await recruiterList.ClickNewAsync();

        await recruiterEdit.FillAgencyNameAsync(agencyName);
        await recruiterEdit.FillContactNameAsync("Jane Doe");
        await recruiterEdit.FillContactEmailAsync("jane.doe@agency.example");
        await recruiterEdit.FillContactTelephoneAsync("07700 900123");
        await recruiterEdit.SaveAsync();

        Assert.True(await recruiterList.HasItemAsync(agencyName),
            $"Expected the new external recruiter '{agencyName}' to appear in the list after creation");
    }

    [Fact]
    public async Task EditExternalRecruiter_PersistsAcrossReload()
    {
        var originalName = $"E2E Recruiter Edit {Guid.NewGuid().ToString("N")[..8]}";
        var updatedName  = $"{originalName} Updated";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var recruiterList = new ExternalRecruiterListPage(_page, _fixture.WebBaseUrl);
        var recruiterEdit = new ExternalRecruiterDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await recruiterList.GoToAsync(AcmeId);
        await recruiterList.ClickNewAsync();
        await recruiterEdit.FillAgencyNameAsync(originalName);
        await recruiterEdit.SaveAsync();

        await recruiterList.GoToAsync(AcmeId);
        await recruiterList.ClickRecruiterAsync(originalName);

        await recruiterEdit.FillAgencyNameAsync(updatedName);
        await recruiterEdit.SaveAsync();

        await recruiterList.GoToAsync(AcmeId);
        await recruiterList.ClickRecruiterAsync(updatedName);

        // Reload the page directly to confirm the change persisted server-side, not just in local state.
        await _page.ReloadAsync();
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

        Assert.Equal(updatedName, await recruiterEdit.GetAgencyNameAsync());
    }

    [Fact]
    public async Task DeactivateThenReactivateExternalRecruiter_TogglesActiveStatus()
    {
        var agencyName = $"E2E Recruiter Deact {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var recruiterList = new ExternalRecruiterListPage(_page, _fixture.WebBaseUrl);
        var recruiterEdit = new ExternalRecruiterDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await recruiterList.GoToAsync(AcmeId);
        await recruiterList.ClickNewAsync();
        await recruiterEdit.FillAgencyNameAsync(agencyName);
        await recruiterEdit.SaveAsync();

        await recruiterList.GoToAsync(AcmeId);
        Assert.True(await recruiterList.IsActiveAsync(agencyName), "Expected newly created recruiter to be Active");
        await recruiterList.DeactivateAsync(agencyName);

        await recruiterList.ShowInactiveAsync();
        Assert.True(await recruiterList.HasItemAsync(agencyName),
            "Expected deactivated recruiter to appear when 'Show inactive' is enabled");
        Assert.False(await recruiterList.IsActiveAsync(agencyName), "Expected deactivated recruiter to show as inactive");

        await recruiterList.ActivateAsync(agencyName);
        Assert.True(await recruiterList.IsActiveAsync(agencyName), "Expected reactivated recruiter to show as active again");
    }

    [Fact]
    public async Task DuplicateAgencyName_ShowsNonBlockingWarning_ButStillSaves()
    {
        var agencyName = $"E2E Recruiter Dup {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var recruiterList = new ExternalRecruiterListPage(_page, _fixture.WebBaseUrl);
        var recruiterEdit = new ExternalRecruiterDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // Create the first recruiter with this agency name.
        await recruiterList.GoToAsync(AcmeId);
        await recruiterList.ClickNewAsync();
        await recruiterEdit.FillAgencyNameAsync(agencyName);
        await recruiterEdit.SaveAsync();

        // Attempt to create a second recruiter with the same (exact) agency name.
        await recruiterList.GoToAsync(AcmeId);
        await recruiterList.ClickNewAsync();
        await recruiterEdit.FillAgencyNameAsync(agencyName);
        await recruiterEdit.BlurAgencyNameAsync();

        Assert.True(await recruiterEdit.IsDuplicateWarningVisibleAsync(),
            "Expected a soft duplicate-agency-name warning after blurring an exact-match agency name");

        // The warning is advisory only — saving must still succeed.
        await recruiterEdit.SaveAsync();

        Assert.True(await recruiterList.HasItemAsync(agencyName),
            "Expected the duplicate-named recruiter to still save successfully despite the warning");
    }
}
