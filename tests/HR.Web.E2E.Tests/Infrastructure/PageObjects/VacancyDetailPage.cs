using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the vacancy create/edit/view page, including its Applications and
/// Interviews tabs (VacancyApplicationsTab.razor / VacancyInterviewsTab.razor), which render
/// inline on this same page rather than as separate routes.
/// Routes: /companies/{id}/vacancies/new, /vacancies/{id}, /vacancies/{id}/view
/// </summary>
public sealed class VacancyDetailPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/vacancies/new");
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid vacancyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/vacancies/{vacancyId}");
        await page.WaitForSelectorAsync(".e-tab, span[role='combobox']", new() { Timeout = 20_000 });
    }

    // ── Overview (new/edit form) ─────────────────────────────────────────────────

    public Task FillTitleAsync(string value) =>
        page.GetByPlaceholder("e.g. Senior Software Engineer").FillAsync(value);

    public Task FillLocationAsync(string value) =>
        page.GetByPlaceholder("e.g. Remote").FillAsync(value);

    /// <summary>Selects a value from the Hiring Manager dropdown (AllowFiltering enabled).</summary>
    public async Task SelectHiringManagerAsync(string nameFragment)
    {
        var group = page.Locator(".col-md-6").Filter(new() { HasText = "Hiring Manager" }).First;
        await group.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        var filterInput = page.Locator(".e-popup.e-ddl:visible input.e-input").First;
        await filterInput.FillAsync(nameFragment);
        await page.WaitForSelectorAsync(".e-popup.e-ddl .e-list-item:not(.e-hide)", new() { Timeout = 15_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item:not(.e-hide)")
            .Filter(new() { HasText = nameFragment })
            .First
            .ClickAsync();
    }

    public async Task SaveNewVacancyAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForURLAsync("**/vacancies", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task<bool> HasErrorAsync() =>
        await page.Locator(".alert-danger, .validation-message").First.IsVisibleAsync();

    public Task<string> GetTitleAsync() =>
        page.GetByPlaceholder("e.g. Senior Software Engineer").InputValueAsync();

    // ── Close / unsaved-changes prompt (EditPageBase) ────────────────────────────
    // Same shared UnsavedChangesDialog.razor component used by every EditPageBase-derived
    // page (see DepartmentEditPage.cs for the representative test coverage of this behavior).

    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.IsVisibleAsync();

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await page.WaitForURLAsync("**/vacancies", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/vacancies", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public async Task CloseAndWaitForListAsync()
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync("**/vacancies", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    // ── Tabs ──────────────────────────────────────────────────────────────────────

    public async Task OpenApplicationsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Applications" }).ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='vacancy-applications-tab']", new() { Timeout = 15_000 });
    }

    public async Task OpenInterviewsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Interviews" }).ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='vacancy-interviews-tab']", new() { Timeout = 15_000 });
    }

    // ── Applications tab: Add Candidate ──────────────────────────────────────────

    public async Task ClickAddCandidateAsync()
    {
        await page.Locator("[data-testid='add-application-btn']").ClickAsync();
        await page.Locator("[role='dialog'].add-application-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task SelectCandidateInAddDialogAsync(string nameOrEmailFragment)
    {
        var dialog = page.Locator(".add-application-dialog");
        await dialog.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        var filterInput = page.Locator(".e-popup.e-ddl:visible input.e-input").First;
        await filterInput.FillAsync(nameOrEmailFragment);
        await page.WaitForSelectorAsync(".e-popup.e-ddl .e-list-item:not(.e-hide)", new() { Timeout = 15_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item:not(.e-hide)")
            .Filter(new() { HasText = nameOrEmailFragment })
            .First
            .ClickAsync();
    }

    public async Task SubmitAddApplicationAsync()
    {
        await page.Locator(".add-application-dialog .e-footer-content button:has-text('Add')").ClickAsync();
        await page.Locator("[role='dialog'].add-application-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    // ── Applications tab: per-row actions ────────────────────────────────────────

    private ILocator ApplicationRow(string candidateNameFragment) =>
        page.Locator(".e-grid .e-row").Filter(new() { HasText = candidateNameFragment });

    public async Task<string?> GetApplicationStatusAsync(string candidateNameFragment)
    {
        var badge = ApplicationRow(candidateNameFragment).First.Locator(".badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    public Task ClickScheduleInterviewForAsync(string candidateNameFragment) =>
        ApplicationRow(candidateNameFragment).First.GetByRole(AriaRole.Button, new() { Name = "Schedule Interview" }).ClickAsync();

    public Task ClickOfferForAsync(string candidateNameFragment) =>
        ApplicationRow(candidateNameFragment).First.GetByRole(AriaRole.Button, new() { Name = "Offer" }).ClickAsync();

    public Task ClickRejectForAsync(string candidateNameFragment) =>
        ApplicationRow(candidateNameFragment).First.GetByRole(AriaRole.Button, new() { Name = "Reject" }).ClickAsync();

    public Task ClickWithdrawForAsync(string candidateNameFragment) =>
        ApplicationRow(candidateNameFragment).First.GetByRole(AriaRole.Button, new() { Name = "Withdraw" }).ClickAsync();

    public Task ClickHireForAsync(string candidateNameFragment) =>
        ApplicationRow(candidateNameFragment).First.GetByRole(AriaRole.Button, new() { Name = "Hire" }).ClickAsync();

    // ── Schedule Interview dialog ────────────────────────────────────────────────

    public async Task WaitForScheduleDialogAsync() =>
        await page.Locator("[role='dialog'].schedule-interview-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

    public async Task SelectInterviewerAsync(string nameFragment)
    {
        var dialog = page.Locator(".schedule-interview-dialog");
        await dialog.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        var filterInput = page.Locator(".e-popup.e-ddl:visible input.e-input").First;
        await filterInput.FillAsync(nameFragment);
        await page.WaitForSelectorAsync(".e-popup.e-ddl .e-list-item:not(.e-hide)", new() { Timeout = 15_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item:not(.e-hide)")
            .Filter(new() { HasText = nameFragment })
            .First
            .ClickAsync();
    }

    public async Task FillScheduledAtAsync(string ddMMyyyyHHmm)
    {
        var input = page.Locator(".schedule-interview-dialog input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyyHHmm);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SubmitScheduleInterviewAsync()
    {
        await page.Locator(".schedule-interview-dialog .e-footer-content button:has-text('Schedule')").ClickAsync();
        await page.Locator("[role='dialog'].schedule-interview-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    // ── Reject dialog ────────────────────────────────────────────────────────────

    public async Task SubmitRejectAsync()
    {
        await page.Locator(".reject-candidate-dialog .e-footer-content button:has-text('Reject')").ClickAsync();
        await page.Locator("[role='dialog'].reject-candidate-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    // ── Hire dialog ───────────────────────────────────────────────────────────────

    public async Task WaitForHireDialogAsync() =>
        await page.Locator("[role='dialog'].hire-candidate-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

    public async Task FillHireStartDateAsync(string ddMMyyyy)
    {
        var input = page.Locator(".hire-candidate-dialog .e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillHireDateOfBirthAsync(string ddMMyyyy)
    {
        var input = page.Locator(".hire-candidate-dialog .e-date-wrapper input.e-input").Nth(1);
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SelectHireNationalityAsync(string nationality)
    {
        var dialog = page.Locator(".hire-candidate-dialog");
        await dialog.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        var filterInput = page.Locator(".e-popup.e-ddl:visible input.e-input").First;
        await filterInput.FillAsync(nationality);
        await page.WaitForSelectorAsync(".e-popup.e-ddl .e-list-item:not(.e-hide)", new() { Timeout = 15_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item:not(.e-hide)")
            .Filter(new() { HasText = nationality })
            .First
            .ClickAsync();
    }

    public async Task SelectHireGenderAsync(string gender)
    {
        var dialog = page.Locator(".hire-candidate-dialog");
        await dialog.Locator("span[role='combobox']").Nth(1).ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = gender })
            .First
            .ClickAsync();
    }

    public async Task SubmitHireAsync()
    {
        await page.Locator(".hire-candidate-dialog .e-footer-content button:has-text('Hire')").ClickAsync();
        await page.Locator("[role='dialog'].hire-candidate-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
    }

    public async Task<bool> HasDialogErrorAsync(string dialogCssClass) =>
        await page.Locator($".{dialogCssClass} .alert-danger").IsVisibleAsync();

    public async Task<string?> GetActionSuccessMessageAsync()
    {
        var alert = page.Locator("[data-testid='vacancy-applications-tab'] .alert-success").First;
        return await alert.IsVisibleAsync() ? (await alert.TextContentAsync())?.Trim() : null;
    }

    // ── Interviews tab ────────────────────────────────────────────────────────────

    private ILocator InterviewRow(string candidateNameFragment) =>
        page.Locator("[data-testid='vacancy-interviews-tab'] .e-grid .e-row").Filter(new() { HasText = candidateNameFragment });

    public async Task<string?> GetInterviewOutcomeAsync(string candidateNameFragment)
    {
        var badge = InterviewRow(candidateNameFragment).First.Locator(".badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    public Task ClickRecordOutcomeForAsync(string candidateNameFragment) =>
        InterviewRow(candidateNameFragment).First.GetByRole(AriaRole.Button, new() { Name = "Record Outcome" }).ClickAsync();

    public async Task WaitForOutcomeDialogAsync() =>
        await page.Locator("[role='dialog'].record-outcome-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

    public async Task SelectOutcomeAsync(string outcome)
    {
        var dialog = page.Locator(".record-outcome-dialog");
        await dialog.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = outcome })
            .First
            .ClickAsync();
    }

    public async Task SubmitOutcomeAsync()
    {
        await page.Locator(".record-outcome-dialog .e-footer-content button:has-text('Save')").ClickAsync();
        await page.Locator("[role='dialog'].record-outcome-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }
}
