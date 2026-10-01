using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class CandidateEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/candidates/new");
        await page.WaitForSelectorAsync("input[placeholder='First name']", new() { Timeout = 20_000 });
    }

    public async Task GoToNewAsync(Guid companyId, string origin)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/candidates/new?origin={origin}");
        await page.WaitForSelectorAsync("input[placeholder='First name']", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid candidateId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/candidates/{candidateId}");
        await page.WaitForSelectorAsync("input[placeholder='First name']", new() { Timeout = 20_000 });
    }

    public async Task FillFirstNameAsync(string value)
    {
        await page.GetByPlaceholder("First name").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillLastNameAsync(string value)
    {
        await page.GetByPlaceholder("Last name").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillEmailAsync(string value)
    {
        await page.GetByPlaceholder("candidate@example.com").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillPhoneAsync(string value)
    {
        await page.GetByPlaceholder("e.g. 07700 900000").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task ClickSaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^(Save|Add)$") }).ClickAsync();
    }

    public async Task SaveNewCandidateAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^(Save|Add)$") }).ClickAsync();
        await page.WaitForURLAsync("**/candidates", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task SaveNewCandidateAndWaitForUrlAsync(string urlGlob)
    {
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^(Save|Add)$") }).ClickAsync();
        await page.WaitForURLAsync(urlGlob, new() { Timeout = 30_000 });
    }

    public async Task CloseAndWaitForUrlAsync(string urlGlob)
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync(urlGlob, new() { Timeout = 30_000 });
    }

    public async Task<bool> HasErrorAsync()
    {
        try
        {
            await page.Locator(".alert-danger, .validation-message").First.WaitForAsync(new() { Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<bool> HasHiredBannerAsync()
    {
        try
        {
            await Assertions.Expect(page.Locator(".alert-success:has-text('hired and linked')"))
                .ToBeVisibleAsync(new() { Timeout = 15_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public Task<string> GetFirstNameAsync() =>
        page.GetByPlaceholder("First name").InputValueAsync();

    public Guid GetIdFromUrl() => UrlIdParser.LastGuid(page.Url);

    public async Task SetPhoneAsync(string value)
    {
        var input = page.GetByPlaceholder("e.g. 07700 900000");
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await page.WaitForTimeoutAsync(150);
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value, new() { Delay = 30 });
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(300);
    }

    public Task<string> GetPhoneAsync() =>
        page.GetByPlaceholder("e.g. 07700 900000").InputValueAsync();

    public async Task<string> WaitForPhoneAsync(string expected)
    {
        var input = page.GetByPlaceholder("e.g. 07700 900000");
        await Assertions.Expect(input).ToHaveValueAsync(expected, new() { Timeout = 15_000 });
        return await input.InputValueAsync();
    }

    public async Task SaveAndWaitForListAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^(Save|Add)$") }).ClickAsync();
        await page.WaitForURLAsync("**/candidates", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    private ILocator ConcurrencyWarningBanner =>
        page.Locator(".save-conflict-banner[role='alert']")
            .Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }) });

    public async Task SaveExpectingConflictAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }

    public Task<bool> IsConcurrencyWarningVisibleAsync() =>
        ConcurrencyWarningBanner.IsVisibleAsync();

    public async Task ClickReloadLatestValuesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }).ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
        await page.WaitForTimeoutAsync(300);
    }


    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^(Close|Cancel)$") }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.WaitUntilVisibleAsync();

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await page.WaitForURLAsync("**/candidates", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/candidates", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public async Task CloseAndWaitForListAsync()
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync("**/candidates", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }


    public Task<bool> HasInactiveBannerAsync() =>
        page.Locator(".alert-secondary:has-text('inactive')").WaitUntilVisibleAsync();

    /// <summary>Text of the inactive banner, including the "Reason: ..." suffix when present.</summary>
    public Task<string?> GetInactiveBannerTextAsync() =>
        page.Locator(".alert-secondary:has-text('inactive')").TextContentAsync();

    public async Task<string?> GetActionErrorAsync()
    {
        var locator = page.Locator(".alert-danger.alert-dismissible");
        if (!await locator.WaitUntilVisibleAsync())
            return null;
        return (await locator.TextContentAsync())?.Trim();
    }

    public Task ClickDeactivateAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Deactivate", Exact = true }).ClickAsync();

    public Task ClickReactivateAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Reactivate", Exact = true }).ClickAsync();

    private ILocator DeactivateDialog => page.Locator("[role='dialog']:has-text('Deactivate Candidate')");

    public Task<bool> IsDeactivateDialogVisibleAsync() => DeactivateDialog.WaitUntilVisibleAsync();

    public Task FillDeactivateReasonAsync(string reason) =>
        DeactivateDialog.Locator("textarea").FillAsync(reason);

    public Task ClickConfirmDeactivateAsync() =>
        DeactivateDialog.GetByRole(AriaRole.Button, new() { Name = "Deactivate", Exact = true }).ClickAsync();

    public async Task ConfirmDeactivateAndCloseAsync()
    {
        await ClickConfirmDeactivateAsync();
        await DeactivateDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public Task CancelDeactivateAsync() =>
        DeactivateDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public Task<bool> HasDeactivateReasonErrorAsync() =>
        DeactivateDialog.Locator(".text-danger.small").WaitUntilVisibleAsync();

    private ILocator ReactivateDialog => page.GetByRole(AriaRole.Dialog).Filter(new() { HasText = "Reactivate Candidate" });

    public Task<bool> IsReactivateDialogVisibleAsync() => ReactivateDialog.WaitUntilVisibleAsync();

    public async Task ConfirmReactivateAsync()
    {
        var confirmButton = ReactivateDialog.GetByRole(AriaRole.Button, new() { Name = "Reactivate", Exact = true });
        await confirmButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await confirmButton.ClickAsync();
        await ReactivateDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public Task CancelReactivateAsync() =>
        ReactivateDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    // ── Internal recruitment Ticket 6: Applications card + internal-applicant alert ──
    // The applications list loads asynchronously after the form renders, so every read here is a
    // web-first expectation. Rows are addressed by data-application-id, never by position.

    private ILocator ApplicationsCard => page.Locator("[data-testid='candidate-applications-card']");

    private ILocator CandidateApplicationRow(Guid applicationId) =>
        ApplicationsCard.Locator($"li[data-testid='candidate-application-row'][data-application-id='{applicationId}']");

    public async Task ExpectApplicationRowAsync(Guid applicationId, string vacancyTitle, bool isInternal)
    {
        var row = CandidateApplicationRow(applicationId);
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(row).ToHaveAttributeAsync("data-internal", isInternal ? "true" : "false");
        await Assertions.Expect(row.Locator("[data-testid='candidate-application-vacancy']")).ToHaveTextAsync(vacancyTitle);

        var badge = row.Locator("[data-testid='internal-application-badge']");
        if (isInternal)
        {
            await Assertions.Expect(badge).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Assertions.Expect(badge).ToHaveTextAsync("Internal");
        }
        else
        {
            await Assertions.Expect(badge).ToHaveCountAsync(0);
        }
    }

    public Task ExpectApplicationRowStageAsync(Guid applicationId, string expectedStage) =>
        Assertions.Expect(CandidateApplicationRow(applicationId).Locator("[data-testid='candidate-application-stage']"))
            .ToHaveTextAsync(expectedStage, new() { Timeout = 15_000 });

    public Task ExpectApplicationRowCountAsync(int expectedCount) =>
        Assertions.Expect(ApplicationsCard.Locator("li[data-testid='candidate-application-row']"))
            .ToHaveCountAsync(expectedCount, new() { Timeout = 30_000 });

    public async Task ExpectInternalApplicantAlertAsync(bool visible)
    {
        var alert = page.Locator("[data-testid='candidate-internal-applicant-alert']");
        if (visible)
        {
            await Assertions.Expect(alert).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Assertions.Expect(alert).ToContainTextAsync("This candidate is a current employee (internal applicant).");
        }
        else
        {
            await Assertions.Expect(alert).ToHaveCountAsync(0);
        }
    }

    public Task ExpectNoHiredAlertAsync() =>
        Assertions.Expect(page.Locator("[data-testid='candidate-hired-alert']")).ToHaveCountAsync(0);
}
