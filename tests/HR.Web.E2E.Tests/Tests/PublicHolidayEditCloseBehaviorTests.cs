using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Direct coverage of the Close / unsaved-changes prompt (EditPageBase) on the Public Holiday
/// edit page.
/// </summary>
[Collection("E2E")]
public sealed class PublicHolidayEditCloseBehaviorTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task Close_ExistingRecordWithNoChanges_NavigatesDirectlyToList()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var holList  = new PublicHolidayListPage(_page, _fixture.WebBaseUrl);
        var holEdit  = new PublicHolidayEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var holName = $"E2E Close {Guid.NewGuid().ToString("N")[..8]}";
        await holList.GoToAsync(AcmeId);
        await holList.ClickNewPublicHolidayAsync();
        await holEdit.FillDateAsync("25/12/2027");
        await holEdit.FillNameAsync(holName);
        await holEdit.FillCountryCodeAsync("GB");
        await holEdit.SaveAsync();

        await holList.GoToAsync(AcmeId);
        var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = holName }).First.GetAttributeAsync("href");
        Assert.NotNull(href);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{href}");
        await _page.WaitForSelectorAsync(".e-date-wrapper", new() { Timeout = 20_000 });

        await holEdit.CloseAndWaitForListAsync();

        Assert.EndsWith("/public-holidays", _page.Url);
    }

    [Fact]
    public async Task Close_NewRecordWithUnsavedChanges_ShowsConfirmDialog()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var holEdit = new PublicHolidayEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await holEdit.GoToNewAsync(AcmeId);
        await holEdit.FillNameAsync("Unsaved Holiday");

        await holEdit.ClickCloseAsync();

        Assert.True(await holEdit.IsUnsavedChangesDialogVisibleAsync());
        Assert.Contains("/public-holidays/new", _page.Url);
    }

    [Fact]
    public async Task Close_DiscardChanges_NavigatesAwayWithoutSaving()
    {
        var holName = $"E2E Discard {Guid.NewGuid().ToString("N")[..8]}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var holList = new PublicHolidayListPage(_page, _fixture.WebBaseUrl);
        var holEdit = new PublicHolidayEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await holEdit.GoToNewAsync(AcmeId);
        await holEdit.FillDateAsync("25/12/2028");
        await holEdit.FillNameAsync(holName);
        await holEdit.FillCountryCodeAsync("GB");

        await holEdit.ClickCloseAsync();
        Assert.True(await holEdit.IsUnsavedChangesDialogVisibleAsync());

        await holEdit.ConfirmDiscardChangesAsync();

        Assert.EndsWith("/public-holidays", _page.Url);
        await holList.GoToAsync(AcmeId);
        Assert.False(await holList.HasHolidayAsync(holName));
    }

    [Fact]
    public async Task Close_SaveFromUnsavedChangesDialog_SavesAndNavigatesToList()
    {
        var holName = $"E2E SaveOnClose {Guid.NewGuid().ToString("N")[..8]}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var holList = new PublicHolidayListPage(_page, _fixture.WebBaseUrl);
        var holEdit = new PublicHolidayEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await holEdit.GoToNewAsync(AcmeId);
        await holEdit.FillDateAsync("25/12/2029");
        await holEdit.FillNameAsync(holName);
        await holEdit.FillCountryCodeAsync("GB");

        await holEdit.ClickCloseAsync();
        Assert.True(await holEdit.IsUnsavedChangesDialogVisibleAsync());

        await holEdit.ConfirmSaveFromUnsavedChangesDialogAsync();

        Assert.EndsWith("/public-holidays", _page.Url);
        await holList.GoToAsync(AcmeId);
        Assert.True(await holList.HasHolidayAsync(holName));
    }

    [Fact]
    public async Task Close_CancelUnsavedChangesDialog_StaysOnPageWithFieldIntact()
    {
        var holName = $"E2E CancelClose {Guid.NewGuid().ToString("N")[..8]}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var holEdit = new PublicHolidayEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await holEdit.GoToNewAsync(AcmeId);
        await holEdit.FillNameAsync(holName);

        await holEdit.ClickCloseAsync();
        Assert.True(await holEdit.IsUnsavedChangesDialogVisibleAsync());

        await holEdit.CancelUnsavedChangesDialogAsync();

        Assert.Contains("/public-holidays/new", _page.Url);
        Assert.Equal(holName, await holEdit.GetNameAsync());
    }
}
