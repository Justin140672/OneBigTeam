using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Recruitment stage edit page: footer "Cancel" (clean vs dirty), "Stay on page" / "Discard changes",
/// and view mode's "Back to recruitment stages". Read-only against the shared stage list, but kept in
/// the "RecruitmentStageList" collection so it never observes RecruitmentStageManagementTests
/// mid-mutation.
/// </summary>
[Collection("RecruitmentStageList")]
public sealed class RecruitmentStageCancelTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = RecruitmentSeedApi.AcmeId;

    private const string MarcusEmail = "marcus.diallo@acme.example";

    private ILocator UnsavedDialog => _page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    private async Task LoginAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
    }

    [Fact]
    public async Task NewStage_CleanCancel_NavigatesToListWithoutDialog()
    {
        await LoginAsync();
        var edit = new RecruitmentStageEditPage(_page, _fixture.WebBaseUrl);
        await edit.GoToNewAsync(AcmeId);

        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true })).ToHaveCountAsync(0);
        await edit.ClickCloseAsync();

        await _page.WaitForURLAsync(new Regex("/recruitment-stages$"), new() { Timeout = 30_000 });
        await Assertions.Expect(UnsavedDialog).ToBeHiddenAsync();
    }

    [Fact]
    public async Task NewStage_DirtyCancel_ShowsDialog_StayKeepsValue_ThenDiscardNavigates()
    {
        await LoginAsync();
        var edit = new RecruitmentStageEditPage(_page, _fixture.WebBaseUrl);
        await edit.GoToNewAsync(AcmeId);
        var name = $"E2E Cancel Stage {Guid.NewGuid().ToString("N")[..8]}";
        await edit.FillNameAsync(name);

        await edit.ClickCloseAsync();
        Assert.True(await edit.IsUnsavedChangesDialogVisibleAsync());
        await Assertions.Expect(UnsavedDialog.GetByRole(AriaRole.Button, new() { Name = "Stay on page", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(UnsavedDialog.GetByRole(AriaRole.Button, new() { Name = "Discard changes", Exact = true })).ToBeVisibleAsync();

        await edit.CancelUnsavedChangesDialogAsync();
        await Assertions.Expect(UnsavedDialog).ToBeHiddenAsync(new() { Timeout = 10_000 });
        Assert.Contains("/recruitment-stages/new", _page.Url);
        Assert.Equal(name, await edit.GetNameAsync());

        await edit.ClickCloseAsync();
        Assert.True(await edit.IsUnsavedChangesDialogVisibleAsync());
        await edit.ConfirmDiscardChangesAsync();

        await _page.WaitForURLAsync(new Regex("/recruitment-stages$"), new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task ExistingStage_DirtyCancel_ShowsDialog_AndViewModeShowsBackToRecruitmentStages()
    {
        await LoginAsync();
        var list = new RecruitmentStageListPage(_page, _fixture.WebBaseUrl);
        var edit = new RecruitmentStageEditPage(_page, _fixture.WebBaseUrl);
        await list.GoToAsync(AcmeId);

        var stageName = (await list.GetNamesInOrderAsync()).First();
        await list.ClickRowLinkAsync(stageName);
        await _page.WaitForURLAsync(new Regex("/recruitment-stages/[0-9a-f-]{36}", RegexOptions.IgnoreCase), new() { Timeout = 30_000 });
        var stageId = edit.GetIdFromUrl();
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        await edit.FillNameAsync($"{stageName} edited");
        await edit.ClickCloseAsync();
        Assert.True(await edit.IsUnsavedChangesDialogVisibleAsync());
        await edit.CancelUnsavedChangesDialogAsync();
        await Assertions.Expect(UnsavedDialog).ToBeHiddenAsync(new() { Timeout = 10_000 });

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/recruitment-stages/{stageId}/view");
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Back to recruitment stages", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true })).ToHaveCountAsync(0);
    }
}
