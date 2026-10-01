using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Vacancy form footer and header actions: "Cancel" (clean vs dirty, "Stay on page" / "Discard
/// changes"), breadcrumb and browser-Back guards, view mode's "Back to vacancies", and the header
/// "Close Vacancy" action (confirmation dialog, cancel, keyboard path) — which must stay a distinct
/// control from the footer Cancel.
///
/// Every close-vacancy test seeds its own Open vacancy through the API and only ever closes that one.
/// </summary>
public sealed class VacancyCancelCloseTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = RecruitmentSeedApi.AcmeId;

    private const string MarcusEmail = "marcus.diallo@acme.example";

    private ILocator UnsavedDialog => _page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    private ILocator ConfirmDialog =>
        _page.GetByRole(AriaRole.Dialog, new() { Name = "Close vacancy" });

    private ILocator CloseVacancyButton => _page.Locator("#close-vacancy-btn");

    private async Task LoginAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
    }

    private async Task<(Guid VacancyId, VacancyDetailPage Detail)> OpenSeededVacancyAsync(string route = "")
    {
        using var seed = await RecruitmentSeedApi.CreateAsync(_fixture.ApiBaseUrl);
        var vacancy = await seed.CreateOpenVacancyAsync();

        await LoginAsync();
        var detail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/vacancies/{vacancy.Id}{route}");
        await Assertions.Expect(_page.GetByRole(AriaRole.Tab, new() { Name = "Overview" }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        return (vacancy.Id, detail);
    }

    private async Task<VacancyDetailPage> OpenNewVacancyAsync()
    {
        await LoginAsync();
        var detail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);
        await detail.GoToNewAsync(AcmeId);
        return detail;
    }

    [Fact]
    public async Task NewVacancy_CleanCancel_NavigatesToListWithoutDialog()
    {
        await OpenNewVacancyAsync();

        await _page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

        await _page.WaitForURLAsync(new Regex("/vacancies$"), new() { Timeout = 30_000 });
        await Assertions.Expect(UnsavedDialog).ToBeHiddenAsync();
    }

    [Fact]
    public async Task NewVacancy_DirtyCancel_ShowsDialogWithStayAndDiscardChoices()
    {
        var detail = await OpenNewVacancyAsync();
        await detail.FillTitleAsync("Unsaved Cancel Check");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

        Assert.True(await detail.IsUnsavedChangesDialogVisibleAsync());
        await Assertions.Expect(UnsavedDialog.GetByRole(AriaRole.Button, new() { Name = "Stay on page", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(UnsavedDialog.GetByRole(AriaRole.Button, new() { Name = "Discard changes", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(UnsavedDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(UnsavedDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(UnsavedDialog).ToContainTextAsync("stay on this page to keep editing");
    }

    [Fact]
    public async Task NewVacancy_DirtyCancel_StayKeepsTypedValueAndUrl()
    {
        var detail = await OpenNewVacancyAsync();
        var title = $"Stay Value {Guid.NewGuid().ToString("N")[..6]}";
        await detail.FillTitleAsync(title);

        await _page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        Assert.True(await detail.IsUnsavedChangesDialogVisibleAsync());
        await detail.CancelUnsavedChangesDialogAsync();

        await Assertions.Expect(UnsavedDialog).ToBeHiddenAsync(new() { Timeout = 10_000 });
        Assert.Contains("/vacancies/new", _page.Url);
        Assert.Equal(title, await detail.GetTitleAsync());
    }

    [Fact]
    public async Task NewVacancy_DirtyCancel_DiscardNavigatesToList()
    {
        var detail = await OpenNewVacancyAsync();
        await detail.FillTitleAsync($"Discard Value {Guid.NewGuid().ToString("N")[..6]}");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        Assert.True(await detail.IsUnsavedChangesDialogVisibleAsync());
        await detail.ConfirmDiscardChangesAsync();

        await _page.WaitForURLAsync(new Regex("/vacancies$"), new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task NewVacancy_DirtyBreadcrumbLink_ShowsDialog_AndStayKeepsPage()
    {
        var detail = await OpenNewVacancyAsync();
        await detail.FillTitleAsync("Breadcrumb Guard");

        await _page.Locator(".e-breadcrumb").GetByRole(AriaRole.Link, new() { Name = "Vacancies", Exact = true }).ClickAsync();

        Assert.True(await detail.IsUnsavedChangesDialogVisibleAsync(),
            "Expected the unsaved-changes dialog when leaving a dirty vacancy through the breadcrumb.");
        Assert.Contains("/vacancies/new", _page.Url);

        await detail.CancelUnsavedChangesDialogAsync();
        await Assertions.Expect(UnsavedDialog).ToBeHiddenAsync(new() { Timeout = 10_000 });
        Assert.Equal("Breadcrumb Guard", await detail.GetTitleAsync());
    }

    [Fact]
    public async Task NewVacancy_DirtyBrowserBack_ShowsDialogOrStaysOnPage()
    {
        await LoginAsync();
        var list = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var detail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await list.GoToAsync(AcmeId);
        await list.ClickNewVacancyAsync();
        await detail.FillTitleAsync("Browser Back Guard");

        await _page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit });

        var dialogShown = await detail.IsUnsavedChangesDialogVisibleAsync();
        Assert.True(dialogShown || _page.Url.Contains("/vacancies/new"),
            "Browser Back from a dirty new vacancy neither showed the unsaved-changes dialog nor stayed on the page.");
        if (dialogShown)
            Assert.Equal("Browser Back Guard", await detail.GetTitleAsync());
    }

    [Fact]
    public async Task ViewMode_ShowsBackToVacancies_WithoutCancelCloseOrSave()
    {
        await OpenSeededVacancyAsync("/view");

        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Back to vacancies", Exact = true }))
            .ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(CloseVacancyButton).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task EditMode_FooterHasCancel_NoBareClose_AndCloseVacancyIsADistinctControl()
    {
        await OpenSeededVacancyAsync();

        var cancel = _page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });
        await Assertions.Expect(cancel).ToHaveCountAsync(1);
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Back to vacancies", Exact = true })).ToHaveCountAsync(0);

        await Assertions.Expect(CloseVacancyButton).ToBeVisibleAsync();
        await Assertions.Expect(CloseVacancyButton).ToHaveTextAsync("Close Vacancy");
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new() { Name = "Close Vacancy", Exact = true })).ToHaveCountAsync(1);

        var sameElement = await cancel.EvaluateAsync<bool>(
            "(el, id) => el === document.getElementById(id)", "close-vacancy-btn");
        Assert.False(sameElement);
    }

    [Fact]
    public async Task CloseVacancy_OpensConfirmationDialog_ExplainingTheEffect()
    {
        await OpenSeededVacancyAsync();

        await CloseVacancyButton.ClickAsync();

        await Assertions.Expect(ConfirmDialog).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(ConfirmDialog).ToContainTextAsync("sets its status to Closed");
        await Assertions.Expect(ConfirmDialog).ToContainTextAsync("Existing applications, interviews and pipeline activity are not changed or deleted");
        await Assertions.Expect(ConfirmDialog.GetByRole(AriaRole.Button, new() { Name = "Close vacancy", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(ConfirmDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator(".status-badge").First).ToHaveTextAsync("Open");
    }

    [Fact]
    public async Task CloseVacancy_CancelInDialog_LeavesStatusUnchanged_AndRestoresFocusToTrigger()
    {
        await OpenSeededVacancyAsync();

        await CloseVacancyButton.ClickAsync();
        await Assertions.Expect(ConfirmDialog).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await ConfirmDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

        await Assertions.Expect(ConfirmDialog).ToBeHiddenAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(_page.Locator(".status-badge").First).ToHaveTextAsync("Open");
        await Assertions.Expect(CloseVacancyButton).ToBeFocusedAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task CloseVacancy_ConfirmInDialog_ClosesTheVacancy()
    {
        var (vacancyId, _) = await OpenSeededVacancyAsync();

        await CloseVacancyButton.ClickAsync();
        await Assertions.Expect(ConfirmDialog).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await ConfirmDialog.GetByRole(AriaRole.Button, new() { Name = "Close vacancy", Exact = true }).ClickAsync();

        await Assertions.Expect(ConfirmDialog).ToBeHiddenAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(_page.Locator(".status-badge").First).ToHaveTextAsync("Closed", new() { Timeout = 20_000 });
        await Assertions.Expect(CloseVacancyButton).ToHaveCountAsync(0);
        Assert.Contains(vacancyId.ToString(), _page.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CloseVacancy_KeyboardPath_EnterOpensDialog_EscapeCancelsAndRestoresFocus()
    {
        await OpenSeededVacancyAsync();

        await CloseVacancyButton.FocusAsync();
        await Assertions.Expect(CloseVacancyButton).ToBeFocusedAsync();
        await _page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(ConfirmDialog).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Assertions.Expect(ConfirmDialog.Locator(":focus")).ToHaveCountAsync(1, new() { Timeout = 10_000 });
        await _page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(ConfirmDialog).ToBeHiddenAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(_page.Locator(".status-badge").First).ToHaveTextAsync("Open");
        await Assertions.Expect(CloseVacancyButton).ToBeFocusedAsync(new() { Timeout = 10_000 });
    }
}
