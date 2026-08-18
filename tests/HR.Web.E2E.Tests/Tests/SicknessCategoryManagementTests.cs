using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class SicknessCategoryManagementTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string TomEmail   = "tom.williams@acme.example";

    [Fact]
    public async Task ListPage_LoadsAndShowsHeading()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var catList  = new SicknessCategoryListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catList.GoToAsync(AcmeId);

        var heading = _page.Locator("h1");
        await heading.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        var headingText = await heading.First.InnerTextAsync();

        Assert.Contains("Sickness Categories", headingText,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSicknessCategory_AppearsInList()
    {
        var suffix   = Guid.NewGuid().ToString("N")[..8];
        var catName  = $"E2E Sickness {suffix}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var catList  = new SicknessCategoryListPage(_page, _fixture.WebBaseUrl);
        var catEdit  = new SicknessCategoryEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catList.GoToAsync(AcmeId);
        await catList.ClickNewAsync();

        await catEdit.FillNameAsync(catName);
        await catEdit.FillDisplayOrderAsync(99);
        await catEdit.SaveAsync();

        Assert.True(await catList.HasItemAsync(catName),
            $"Expected the new sickness category '{catName}' to appear in the list after creation.");
    }

    [Fact]
    public async Task EditSicknessCategory_UpdatesNameInList()
    {
        var suffix      = Guid.NewGuid().ToString("N")[..8];
        var originalName = $"E2E Edit {suffix}";
        var updatedName  = $"E2E Edited {suffix}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var catList  = new SicknessCategoryListPage(_page, _fixture.WebBaseUrl);
        var catEdit  = new SicknessCategoryEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Create first so we have a category to edit.
        await catList.GoToAsync(AcmeId);
        await catList.ClickNewAsync();
        await catEdit.FillNameAsync(originalName);
        await catEdit.FillDisplayOrderAsync(50);
        await catEdit.SaveAsync();

        Assert.True(await catList.HasItemAsync(originalName),
            $"Pre-condition: expected '{originalName}' to appear in the list.");

        // Edit the category via the link in the grid.
        await catList.ClickEditAsync(originalName);

        // Clear the name field and type the new name.
        var nameInput = _page.GetByPlaceholder("e.g. Cold, Stress, Back Pain");
        await nameInput.ClearAsync();
        await nameInput.FillAsync(updatedName);
        await catEdit.SaveAsync();

        Assert.True(await catList.HasItemAsync(updatedName),
            $"Expected the updated name '{updatedName}' to appear in the list after editing.");
        Assert.False(await catList.HasItemAsync(originalName),
            $"Expected the old name '{originalName}' to no longer appear in the list after editing.");
    }

    [Fact]
    public async Task DeleteSicknessCategory_MarksAsInactive()
    {
        var suffix  = Guid.NewGuid().ToString("N")[..8];
        var catName = $"E2E Del {suffix}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var catList  = new SicknessCategoryListPage(_page, _fixture.WebBaseUrl);
        var catEdit  = new SicknessCategoryEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Create the category to delete.
        await catList.GoToAsync(AcmeId);
        await catList.ClickNewAsync();
        await catEdit.FillNameAsync(catName);
        await catEdit.FillDisplayOrderAsync(1);
        await catEdit.SaveAsync();

        Assert.True(await catList.HasItemAsync(catName),
            $"Pre-condition: expected '{catName}' to appear in the list.");
        Assert.True(await catList.IsActiveAsync(catName),
            $"Pre-condition: expected '{catName}' to be Active.");

        await catList.DeleteAsync(catName);

        // "Delete" is a soft-deactivate. The list defaults to active-only, so the row
        // disappears until "Show Inactive" is toggled — then it reappears as Inactive.
        Assert.False(await catList.HasItemAsync(catName),
            $"Expected '{catName}' to no longer appear in the default active-only view after deletion.");

        await catList.ShowInactiveAsync();

        Assert.True(await catList.HasItemAsync(catName),
            $"Expected '{catName}' to appear once inactive categories are shown.");
        Assert.False(await catList.IsActiveAsync(catName),
            $"Expected '{catName}' to be marked Inactive after deletion.");
    }

    [Fact]
    public async Task PlainEmployee_IsRedirectedAway_FromSicknessCategoriesPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/sickness-categories");
        // See E2ETestBase.WaitForUrlToStopContainingAsync's doc comment: the redirect is a
        // client-side Blazor NavigateTo, not a full page navigation, so NetworkIdle after the
        // initial GET is not a reliable signal that the redirect has completed.
        await WaitForUrlToStopContainingAsync("/sickness-categories");

        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/sickness-categories"),
            $"Expected a plain employee to be redirected away from the sickness categories page, but ended up at: {finalUrl}");
    }
}
