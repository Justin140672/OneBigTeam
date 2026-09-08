using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the employee-facing Employee Directory:
/// - A plain employee can reach it from the My Profile Overview quick action.
/// - It lists the seeded Acme employees.
/// - The search box filters the cards (and shows the empty state for no matches).
/// - Clicking a card opens a read-only detail dialog with no edit controls.
///
/// Uses the plain-employee persona (Tom Williams) since the directory is available to any
/// authenticated employee of the company.
/// </summary>
public sealed class EmployeeDirectoryTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail = "tom.williams@acme.example";

    // Seeded Acme engineer (EmployeesModule.SeedEmployeesAsync — "James Okafor", ACME-002,
    // Engineering department). A stable, unique-in-seed surname to search on.
    private const string SeededColleagueName    = "James Okafor";
    private const string SeededColleagueSurname = "Okafor";

    [Fact]
    public async Task QuickAction_FromMyProfileOverview_NavigatesToDirectory()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile  = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var overview = new OverviewTab(_page);
        var directory = new EmployeeDirectoryPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        if (!await overview.IsVisibleAsync())
            await profile.OpenOverviewTabAsync();
        await overview.WaitForLoadAsync();

        await _page.Locator("[data-testid='employee-directory-action']").ClickAsync();

        await _page.WaitForURLAsync("**/employees/directory", new() { Timeout = 15_000 });
        Assert.Contains("/employees/directory", _page.Url);

        await directory.WaitForInteractiveAsync();
        await Assertions.Expect(directory.Heading).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Directory_Loads_AndShowsSeededEmployeeCards()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var directory = new EmployeeDirectoryPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await directory.GoToAsync(AcmeId);

        await Assertions.Expect(directory.Heading).ToBeVisibleAsync();
        Assert.True(await directory.CardCount() > 0,
            "Expected at least one employee card in the directory for the seeded Acme company");
    }

    [Fact]
    public async Task Directory_Search_FiltersToMatch_AndShowsEmptyStateForNonsense()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var directory = new EmployeeDirectoryPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await directory.GoToAsync(AcmeId);

        // Search a known seeded surname — the matching colleague's card should remain.
        await directory.SearchAsync(SeededColleagueSurname);
        await Assertions.Expect(directory.CardByName(SeededColleagueName)).ToBeVisibleAsync();
        Assert.False(await directory.IsEmptyStateVisibleAsync(),
            "Did not expect the empty state while a known surname is matched");

        // A nonsense search yields the empty state and no cards.
        await directory.SearchAsync($"zzz-no-such-person-{Guid.NewGuid():N}");
        await Assertions.Expect(_page.GetByText("No employees found")).ToBeVisibleAsync();
        Assert.Equal(0, await directory.CardCount());
    }

    [Fact]
    public async Task Directory_ClickingCard_OpensReadOnlyDetailDialog()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var directory = new EmployeeDirectoryPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await directory.GoToAsync(AcmeId);
        await directory.SearchAsync(SeededColleagueSurname);

        var dialog = await directory.OpenCardAsync(SeededColleagueName);

        // Shows the employee's identity and role.
        await Assertions.Expect(dialog.GetByText(SeededColleagueName)).ToBeVisibleAsync();
        await Assertions.Expect(dialog.GetByText("Position")).ToBeVisibleAsync();

        // Read-only: no save button and no editable inputs inside the dialog.
        Assert.Equal(0, await dialog.Locator("button:has-text('Save')").CountAsync());
        Assert.Equal(0, await dialog.Locator("input:not([type='hidden']), textarea, [role='combobox']").CountAsync());
    }
}
