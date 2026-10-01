using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Candidate and external-recruiter forms: footer "Cancel" (clean vs dirty), the unsaved-changes
/// dialog's "Stay on page" / "Discard changes", and view mode's "Back to ..." button. The vacancy
/// form is covered by VacancyCancelCloseTests; recruitment stages by RecruitmentStageCancelTests.
/// </summary>
public sealed class RecruitmentFormCancelTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = RecruitmentSeedApi.AcmeId;

    private const string MarcusEmail = "marcus.diallo@acme.example";

    private ILocator UnsavedDialog => _page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private async Task LoginAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
    }

    private async Task AssertNoBareCloseAsync()
    {
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task NewCandidate_CleanCancel_NavigatesToListWithoutDialog()
    {
        await LoginAsync();
        var edit = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        await edit.GoToNewAsync(AcmeId);

        await AssertNoBareCloseAsync();
        await edit.ClickCloseAsync();

        await _page.WaitForURLAsync(new Regex("/candidates$"), new() { Timeout = 30_000 });
        await Assertions.Expect(UnsavedDialog).ToBeHiddenAsync();
    }

    [Fact]
    public async Task NewCandidate_DirtyCancel_ShowsDialog_StayKeepsValue_ThenDiscardNavigates()
    {
        await LoginAsync();
        var edit = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        await edit.GoToNewAsync(AcmeId);
        var firstName = $"Typed{Unique()}";
        await edit.FillFirstNameAsync(firstName);

        await edit.ClickCloseAsync();
        Assert.True(await edit.IsUnsavedChangesDialogVisibleAsync());
        await Assertions.Expect(UnsavedDialog.GetByRole(AriaRole.Button, new() { Name = "Stay on page", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(UnsavedDialog.GetByRole(AriaRole.Button, new() { Name = "Discard changes", Exact = true })).ToBeVisibleAsync();

        await edit.CancelUnsavedChangesDialogAsync();
        await Assertions.Expect(UnsavedDialog).ToBeHiddenAsync(new() { Timeout = 10_000 });
        Assert.Contains("/candidates/new", _page.Url);
        Assert.Equal(firstName, await edit.GetFirstNameAsync());

        await edit.ClickCloseAsync();
        Assert.True(await edit.IsUnsavedChangesDialogVisibleAsync());
        await _page.Locator("[role='dialog']:has-text('Unsaved Changes')")
            .GetByRole(AriaRole.Button, new() { Name = "Discard changes", Exact = true }).ClickAsync();

        await _page.WaitForURLAsync(new Regex("/candidates$"), new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task ExistingCandidate_EditMode_DirtyCancel_ShowsDialog_AndViewModeShowsBackToCandidates()
    {
        using var seed = await RecruitmentSeedApi.CreateAsync(_fixture.ApiBaseUrl);
        var candidate = await seed.CreateCandidateAsync();

        await LoginAsync();
        var edit = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        await edit.GoToAsync(AcmeId, candidate.Id);

        await AssertNoBareCloseAsync();
        await edit.FillFirstNameAsync($"Edited{Unique()}");
        await edit.ClickCloseAsync();
        Assert.True(await edit.IsUnsavedChangesDialogVisibleAsync());
        await edit.CancelUnsavedChangesDialogAsync();
        await Assertions.Expect(UnsavedDialog).ToBeHiddenAsync(new() { Timeout = 10_000 });

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/candidates/{candidate.Id}/view");
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Back to candidates", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true })).ToHaveCountAsync(0);
        await AssertNoBareCloseAsync();

        await _page.GetByRole(AriaRole.Button, new() { Name = "Back to candidates", Exact = true }).ClickAsync();
        await _page.WaitForURLAsync(new Regex("/candidates$"), new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task NewExternalRecruiter_CleanCancel_NavigatesToListWithoutDialog()
    {
        await LoginAsync();
        var detail = new ExternalRecruiterDetailPage(_page, _fixture.WebBaseUrl);
        await detail.GoToNewAsync(AcmeId);

        await AssertNoBareCloseAsync();
        await detail.ClickCloseAsync();

        await _page.WaitForURLAsync(new Regex("/external-recruiters$"), new() { Timeout = 30_000 });
        await Assertions.Expect(UnsavedDialog).ToBeHiddenAsync();
    }

    [Fact]
    public async Task NewExternalRecruiter_DirtyCancel_ShowsDialog_StayKeepsValue_ThenDiscardNavigates()
    {
        await LoginAsync();
        var detail = new ExternalRecruiterDetailPage(_page, _fixture.WebBaseUrl);
        await detail.GoToNewAsync(AcmeId);
        var agency = $"E2E Cancel Agency {Unique()}";
        await detail.FillAgencyNameAsync(agency);

        await detail.ClickCloseAsync();
        Assert.True(await detail.IsUnsavedChangesDialogVisibleAsync());
        await Assertions.Expect(UnsavedDialog.GetByRole(AriaRole.Button, new() { Name = "Stay on page", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(UnsavedDialog.GetByRole(AriaRole.Button, new() { Name = "Discard changes", Exact = true })).ToBeVisibleAsync();

        await detail.CancelUnsavedChangesDialogAsync();
        await Assertions.Expect(UnsavedDialog).ToBeHiddenAsync(new() { Timeout = 10_000 });
        Assert.Contains("/external-recruiters/new", _page.Url);
        Assert.Equal(agency, await detail.GetAgencyNameAsync());

        await detail.ClickCloseAsync();
        Assert.True(await detail.IsUnsavedChangesDialogVisibleAsync());
        await detail.ConfirmDiscardChangesAsync();

        await _page.WaitForURLAsync(new Regex("/external-recruiters$"), new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task ExistingExternalRecruiter_ViewModeShowsBackToExternalRecruiters()
    {
        using var seed = await RecruitmentSeedApi.CreateAsync(_fixture.ApiBaseUrl);
        var recruiterId = await seed.CreateExternalRecruiterAsync($"E2E View Agency {Unique()}");

        await LoginAsync();
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/external-recruiters/{recruiterId}/view");

        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Back to external recruiters", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true })).ToHaveCountAsync(0);
        await AssertNoBareCloseAsync();

        await _page.GetByRole(AriaRole.Button, new() { Name = "Back to external recruiters", Exact = true }).ClickAsync();
        await _page.WaitForURLAsync(new Regex("/external-recruiters$"), new() { Timeout = 30_000 });
    }
}
