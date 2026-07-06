using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Close button and "unsaved changes" confirmation prompt that <c>EditPageBase</c>
/// provides to every edit page (see EditPageBase.cs / UnsavedChangesDialog.razor). Exercised
/// via the Candidate edit page as a representative host — the behavior under test lives in
/// the shared base class, not in CandidateEdit itself.
/// </summary>
[Collection("E2E")]
public sealed class CandidateEditCloseBehaviorTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task Close_ExistingRecordWithNoChanges_NavigatesDirectlyToList()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Create a candidate first so we have an existing, unmodified record to reopen.
        var unique   = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"E2EClose{unique}";
        await candidateList.GoToAsync(AcmeId);
        await candidateList.ClickNewCandidateAsync();
        await candidateEdit.FillFirstNameAsync("E2E");
        await candidateEdit.FillLastNameAsync(lastName);
        await candidateEdit.FillEmailAsync($"e2e.close{unique}@example.com");
        await candidateEdit.SaveNewCandidateAsync();

        await candidateList.GoToAsync(AcmeId);
        Assert.True(await candidateList.HasCandidateAsync(lastName));

        // Reopening it and clicking Close with no edits should navigate straight back to the
        // list — no "unsaved changes" prompt should appear (the wait inside CloseAndWaitForListAsync
        // would time out if one blocked navigation).
        await candidateList.ClickCandidateAsync(lastName);
        await candidateEdit.CloseAndWaitForListAsync();

        Assert.EndsWith("/candidates", _page.Url);
    }

    [Fact]
    public async Task Close_NewRecordWithUnsavedChanges_ShowsConfirmDialog()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await candidateEdit.GoToNewAsync(AcmeId);
        await candidateEdit.FillFirstNameAsync("Unsaved Candidate");

        await candidateEdit.ClickCloseAsync();

        Assert.True(await candidateEdit.IsUnsavedChangesDialogVisibleAsync(),
            "Expected the unsaved-changes confirmation dialog to appear when closing with edits pending");
        Assert.Contains("/candidates/new", _page.Url);
    }

    [Fact]
    public async Task Close_DiscardChanges_NavigatesAwayWithoutSaving()
    {
        var unique   = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"E2EDiscard{unique}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await candidateEdit.GoToNewAsync(AcmeId);
        await candidateEdit.FillFirstNameAsync("E2E");
        await candidateEdit.FillLastNameAsync(lastName);

        await candidateEdit.ClickCloseAsync();
        Assert.True(await candidateEdit.IsUnsavedChangesDialogVisibleAsync());

        await candidateEdit.ConfirmDiscardChangesAsync();

        Assert.EndsWith("/candidates", _page.Url);

        await candidateList.GoToAsync(AcmeId);
        Assert.False(await candidateList.HasCandidateAsync(lastName),
            "Discarding changes should not have created the candidate");
    }

    [Fact]
    public async Task Close_SaveFromUnsavedChangesDialog_SavesAndNavigatesToList()
    {
        var unique   = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"E2ESaveOnClose{unique}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await candidateEdit.GoToNewAsync(AcmeId);
        await candidateEdit.FillFirstNameAsync("E2E");
        await candidateEdit.FillLastNameAsync(lastName);
        await candidateEdit.FillEmailAsync($"e2e.saveonclose{unique}@example.com");

        await candidateEdit.ClickCloseAsync();
        Assert.True(await candidateEdit.IsUnsavedChangesDialogVisibleAsync());

        await candidateEdit.ConfirmSaveFromUnsavedChangesDialogAsync();

        Assert.EndsWith("/candidates", _page.Url);
        Assert.True(await candidateList.HasCandidateAsync(lastName),
            "Choosing Save from the unsaved-changes dialog should have created the candidate");
    }

    [Fact]
    public async Task Close_CancelUnsavedChangesDialog_StaysOnPageWithFieldIntact()
    {
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var firstName = $"E2ECancelClose{unique}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await candidateEdit.GoToNewAsync(AcmeId);
        await candidateEdit.FillFirstNameAsync(firstName);

        await candidateEdit.ClickCloseAsync();
        Assert.True(await candidateEdit.IsUnsavedChangesDialogVisibleAsync());

        await candidateEdit.CancelUnsavedChangesDialogAsync();

        // Cancelling the prompt should just dismiss it — the user stays on the form with
        // their edits untouched, free to keep editing or click Close again.
        Assert.Contains("/candidates/new", _page.Url);
        Assert.Equal(firstName, await candidateEdit.GetFirstNameAsync());
    }
}
