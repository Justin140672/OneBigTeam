using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class SharedDocumentDetailPage(IPage page, string baseUrl)
{
    private ILocator HeaderActionsGroup => page.Locator(".doc-detail-actions-group");
    private ILocator PublishHeaderButton => HeaderActionsGroup.GetByRole(AriaRole.Button).Filter(new() { Has = page.Locator(".fa-paper-plane") });

    private ILocator ReviewHeaderButton => HeaderActionsGroup.GetByRole(AriaRole.Button).Filter(new() { Has = page.Locator(".fa-clipboard-check") });

    // "Edit details" is a unique button name on the page now (Audience/Acknowledgement cards use
    // their own distinct "Edit audience"/"Edit acknowledgement settings" names — see the renamed
    // locators below), so this no longer needs the old .First-based disambiguation against a
    // shared generic "Edit" name.
    //
    // NOT Exact — confirmed via a captured DOM dump (diag/*_shareddoc-header-missing.html) that
    // this specific SfButton carries aria-label="Edit details for {Title}" (see
    // SharedDocumentDetail.razor). Per the WAI-ARIA accessible-name computation algorithm, a
    // non-empty aria-label completely OVERRIDES an element's visible text content for accessible-
    // name purposes — Playwright's GetByRole Name match is against that computed name, not what's
    // on screen. So the button's real accessible name is "Edit details for {Title}", never exactly
    // "Edit details", and Exact=true could never match it — the button was visible and clickable
    // the entire time; every prior "fix" to GoToAsync's timing/retries was chasing a symptom that
    // couldn't be timing-related, since the target literally didn't have the name being searched
    // for. Non-exact matching is substring-based in Playwright, so "Edit details" now matches
    // against "Edit details for {Title}" correctly.
    private ILocator EditMetadataHeaderButton => page.GetByRole(AriaRole.Button, new() { Name = "Edit details" });
    private ILocator EditMetadataDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Edit Document Metadata" });

    private ILocator PublishDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Publish Document" });
    private ILocator ArchiveDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Archive Document" });
    private ILocator ExpireDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Mark Document as Expired" });
    private ILocator ReviewDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Complete Review" });

    private ILocator AcknowledgementCard => page.Locator(".overview-card").Filter(new() { HasText = "Acknowledgement" }).First;
    private ILocator EditAcknowledgementDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Edit Acknowledgement Settings" });

    private ILocator AudienceCard => page.Locator(".overview-card").Filter(new() { HasText = "Audience" }).First;

    private ILocator MoreActionsButton => page.GetByRole(AriaRole.Button, new() { Name = "More actions" });

    private static string MoreActionsItemId(string itemName) => itemName switch
    {
        "Archive" => "archive",
        "Mark Expired" => "expire",
        "Audit History" => "audit",
        _ => throw new ArgumentOutOfRangeException(nameof(itemName), itemName, "Unknown 'More actions' item name."),
    };

    private async Task ClickMoreActionsItemAsync(string itemName)
    {
        var menuItem = page.Locator($"#{MoreActionsItemId(itemName)}");
        const int maxAttempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await MoreActionsButton.ClickAsync(new() { Timeout = 5_000 });
                await menuItem.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
                await menuItem.ClickAsync(new() { Timeout = 3_000 });
                return;
            }
            catch (Exception ex) when (ex is TimeoutException or PlaywrightException && attempt < maxAttempts)
            {
                await page.Keyboard.PressAsync("Escape");
            }
            catch (TimeoutException ex)
            {
                var popupText = await page.Locator(".e-dropdown-popup").IsVisibleAsync()
                    ? await page.Locator(".e-dropdown-popup").InnerTextAsync()
                    : "(popup not visible)";
                throw new TimeoutException(
                    $"'More actions' item '{itemName}' (#{MoreActionsItemId(itemName)}) could not be clicked after {maxAttempts} attempts. " +
                    $"Popup contents at final failure: {popupText}", ex);
            }
        }
    }

    private async Task<bool> HasMoreActionsItemAsync(string itemName)
    {
        var popup = page.Locator(".e-dropdown-popup:visible");
        var anyItem = popup.Locator("li").First;

        for (var attempt = 1; ; attempt++)
        {
            await MoreActionsButton.ClickAsync();
            try
            {
                await anyItem.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
                break;
            }
            catch (TimeoutException) when (attempt < 4)
            {
                await page.Keyboard.PressAsync("Escape");
            }
        }

        var visible = await page.Locator($"#{MoreActionsItemId(itemName)}").IsVisibleAsync();

        await page.Keyboard.PressAsync("Escape");
        return visible;
    }

    public async Task GoToAsync(Guid companyId, Guid documentId)
    {
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await page.GotoAsync($"{baseUrl}/companies/{companyId}/shared-documents/{documentId}");
            await page.WaitForSelectorAsync("h1.doc-detail-title, .alert-danger", new() { Timeout = 20_000 });

            if (await page.Locator("h1.doc-detail-title").IsVisibleAsync())
            {
                await DumpDiagnosticsIfHeaderActionsMissingAsync();
                return;
            }

            if (attempt < 4)
                await page.WaitForTimeoutAsync(500 * attempt);
        }

        throw new TimeoutException(
            $"SharedDocumentDetailPage.GoToAsync: document {documentId} (company {companyId}) still shows " +
            "'Document was not found' after 4 attempts. Either the id is wrong or the document " +
            "genuinely isn't resolvable via GetSharedCompanyDocumentAsync — this is NOT a rendering-timing issue.");
    }

    private async Task DumpDiagnosticsIfHeaderActionsMissingAsync()
    {
        try
        {
            var editDetails = page.GetByRole(AriaRole.Button, new() { Name = "Edit details" });
            var editAudience = page.GetByRole(AriaRole.Button, new() { Name = "Edit audience" });
            var editAcknowledgement = page.GetByRole(AriaRole.Button, new() { Name = "Edit acknowledgement settings" });

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                if (await editDetails.IsVisibleAsync() && await editAudience.IsVisibleAsync()
                    && await editAcknowledgement.IsVisibleAsync())
                {
                    return;
                }
                await page.WaitForTimeoutAsync(200);
            }

            var dir = Path.Combine(AppContext.BaseDirectory, "diag");
            Directory.CreateDirectory(dir);
            var stamp = $"{DateTime.UtcNow:HHmmss_fff}_{Guid.NewGuid().ToString("N")[..6]}_shareddoc-header-missing";
            await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{stamp}.png"), FullPage = true });
            await File.WriteAllTextAsync(
                Path.Combine(dir, $"{stamp}.html"),
                $"URL: {page.Url}\n\n=== DOM ===\n{await page.ContentAsync()}");
        }
        catch
        {
        }
    }

    public async Task GoToAcknowledgementProgressAsync(Guid companyId, Guid documentId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/shared-documents/{documentId}/acknowledgement-progress");
        await page.WaitForSelectorAsync(".overview-card, .alert-danger", new() { Timeout = 20_000 });
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task<string> GetStatusAsync() =>
        (await page.Locator("dt:has-text('Status') + dd .badge").InnerTextAsync()).Trim();

    public async Task<string> GetTitleAsync() =>
        (await page.Locator("h1").First.InnerTextAsync()).Trim();


    public async Task<string> GetCurrentDocumentFileNameAsync() =>
        (await page.Locator(".doc-current-file-name").InnerTextAsync()).Trim();

    public async Task<string> GetCurrentDocumentMetaTextAsync() =>
        (await page.Locator(".doc-current-file-meta").InnerTextAsync()).Trim();

    public ILocator CurrentDocumentOpenLink => page.Locator(".doc-current-file-actions a").Filter(new() { HasText = "Open" });

    public ILocator CurrentDocumentDownloadLink => page.Locator(".doc-current-file-actions a").Filter(new() { HasText = "Download" });

    public Task<bool> IsUploadNewVersionButtonVisibleAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Upload New Version" }).IsVisibleAsync();

    public async Task<string> GetCategoryAsync() =>
        (await page.Locator("dt:has-text('Category') + dd").InnerTextAsync()).Trim();

    public async Task<string?> GetDescriptionAsync()
    {
        var row = page.Locator("dt:has-text('Description') + dd");
        if (!await row.IsVisibleAsync()) return null;
        return (await row.InnerTextAsync()).Trim();
    }

    private async Task ClearAndTypeAsync(ILocator input, string value)
    {
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await Assertions.Expect(input).ToHaveValueAsync("", new() { Timeout = 5_000 });
        if (value.Length > 0)
        {
            await input.PressSequentiallyAsync(value, new() { Delay = 30 });
            await Assertions.Expect(input).ToHaveValueAsync(value, new() { Timeout = 5_000 });
        }
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task EditTitleDescriptionCategoryAsync(string title, string description, string categoryLabel)
    {
        await OpenMetadataDialogAsync();

        await ClearAndTypeAsync(EditMetadataDialog.GetByPlaceholder("Document title"), title);
        await ClearAndTypeAsync(EditMetadataDialog.GetByPlaceholder("Optional description"), description);

        var categoryGroup = EditMetadataDialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
        await DropDownSelector.SelectAsync(page, categoryGroup, categoryLabel);

        await Assertions.Expect(categoryGroup.Locator(".e-input-group input").First)
            .ToHaveValueAsync(new Regex(Regex.Escape(categoryLabel)), new() { Timeout = 10_000 });

        await EditMetadataDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await Assertions.Expect(page.Locator("h1").First)
            .ToHaveTextAsync(title, new() { Timeout = 15_000 });
    }

    public Task<bool> IsArchiveButtonVisibleAsync() => HasMoreActionsItemAsync("Archive");

    public Task<bool> IsExpireButtonVisibleAsync() => HasMoreActionsItemAsync("Mark Expired");

    public Task<bool> IsPublishButtonVisibleAsync() => PublishHeaderButton.IsVisibleAsync();

    public Task<bool> IsReviewButtonVisibleAsync() => ReviewHeaderButton.IsVisibleAsync();

    public async Task<string?> GetReviewDateTextAsync()
    {
        var row = page.Locator("dt:has-text('Next Review Date') + dd");
        if (!await row.IsVisibleAsync()) return null;
        return (await row.InnerTextAsync()).Trim();
    }

    public async Task<string?> GetReviewFrequencyTextAsync()
    {
        var row = page.Locator("dt:has-text('Review Frequency') + dd");
        if (!await row.IsVisibleAsync()) return null;
        return (await row.InnerTextAsync()).Trim();
    }

    public async Task SetReviewFrequencyAsync(string frequencyLabel, int? customMonths = null)
    {
        await EditMetadataHeaderButton.ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var reviewFrequencyGroup = EditMetadataDialog.Locator(".col-md-6").Filter(new() { HasText = "Review Frequency" });
        await DropDownSelector.SelectAsync(page, reviewFrequencyGroup, frequencyLabel);

        await Assertions.Expect(reviewFrequencyGroup.Locator(".e-input-group input").First)
            .ToHaveValueAsync(new Regex(Regex.Escape(frequencyLabel)), new() { Timeout = 10_000 });

        if (frequencyLabel != "None")
        {
            var reviewDateInput = EditMetadataDialog.Locator(".col-md-6")
                .Filter(new() { HasText = "Next Review Date" })
                .Locator(".e-date-wrapper input.e-input");
            await reviewDateInput.ClickAsync();
            await reviewDateInput.FillAsync(DateOnly.FromDateTime(DateTime.Today.AddYears(1)).ToString("dd/MM/yyyy"));
            await page.Keyboard.PressAsync("Tab");
        }

        if (customMonths.HasValue)
        {
            var monthsInput = EditMetadataDialog.Locator(".col-md-6")
                .Filter(new() { HasText = "Custom Frequency" })
                .Locator("input");
            await monthsInput.ClickAsync();
            await page.Keyboard.PressAsync("Control+A");
            await page.Keyboard.PressAsync("Delete");
            await monthsInput.PressSequentiallyAsync(customMonths.Value.ToString());
            await page.Keyboard.PressAsync("Tab");
        }

        await EditMetadataDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        if (frequencyLabel != "None")
        {
            await Assertions.Expect(page.Locator("dt:has-text('Review Frequency') + dd"))
                .ToContainTextAsync(frequencyLabel == "Custom" ? "Custom" : frequencyLabel, new() { Timeout = 15_000 });
        }

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public Task WaitForReviewOwnerTextAsync(string expected) =>
        Assertions.Expect(page.Locator("dt:has-text('Review Owner') + dd")).ToHaveTextAsync(expected, new() { Timeout = 15_000 });

    public Task WaitForReviewOwnerRowHiddenAsync() =>
        Assertions.Expect(page.Locator("dt:has-text('Review Owner') + dd")).ToBeHiddenAsync(new() { Timeout = 15_000 });

    public async Task<string?> GetReviewOwnerTextAsync()
    {
        var row = page.Locator("dt:has-text('Review Owner') + dd");
        if (!await row.IsVisibleAsync()) return null;
        return (await row.InnerTextAsync()).Trim();
    }

    public async Task SetReviewOwnerAsync(string employeeNameFragment)
    {
        await EditMetadataHeaderButton.ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var reviewOwnerGroup = EditMetadataDialog.Locator(".col-md-6").Filter(new() { HasText = "Review Owner" });
        await DropDownSelector.SelectAsync(page, reviewOwnerGroup, employeeNameFragment);

        await Assertions.Expect(reviewOwnerGroup.Locator(".e-input-group input").First)
            .ToHaveValueAsync(employeeNameFragment, new() { Timeout = 10_000 });

        await page.WaitForTimeoutAsync(300);

        await EditMetadataDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task ClearReviewOwnerAsync()
    {
        await EditMetadataHeaderButton.ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var reviewOwnerGroup = EditMetadataDialog.Locator(".col-md-6").Filter(new() { HasText = "Review Owner" });
        await DropDownSelector.SelectAsync(page, reviewOwnerGroup, "Not assigned");

        await Assertions.Expect(reviewOwnerGroup.Locator(".e-input-group input").First)
            .ToHaveValueAsync("Not assigned", new() { Timeout = 10_000 });

        await page.WaitForTimeoutAsync(300);

        await EditMetadataDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await Assertions.Expect(page.Locator("dt:has-text('Review Owner')"))
            .Not.ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    public async Task PublishAsync()
    {
        await PublishHeaderButton.ClickAsync();
        await PublishDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await PublishDialog.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
        await PublishDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 60_000 });

        await WaitForOverlayToClearAsync();

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task RequireAcknowledgementAsync(DateOnly dueDate)
    {
        await AcknowledgementCard.GetByRole(AriaRole.Button, new() { Name = "Edit acknowledgement settings" }).ClickAsync();
        await EditAcknowledgementDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var checkboxWrapper = EditAcknowledgementDialog.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Requires employee acknowledgement" });
        await checkboxWrapper.Locator("label").ClickAsync();

        var dateInput = EditAcknowledgementDialog.Locator(".e-date-wrapper input.e-input");
        await dateInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForTimeoutAsync(300);

        await dateInput.ClickAsync();
        await dateInput.FillAsync(dueDate.ToString("dd/MM/yyyy"));
        await page.Keyboard.PressAsync("Tab");

        await EditAcknowledgementDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditAcknowledgementDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        await WaitForOverlayToClearAsync();

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    private ILocator AuditHistoryDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Audit History" });
    private ILocator AuditDetailDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Audit Event Detail" });

    public async Task OpenAuditHistoryDialogAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            await ClickMoreActionsItemAsync("Audit History");
            try
            {
                await AuditHistoryDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
                break;
            }
            catch (TimeoutException) when (attempt < 4)
            {
            }
        }

        await AuditHistoryDialog.Locator(
            "[data-testid='document-audit-history-grid'] .e-row, [data-testid='document-audit-history-grid'] .e-emptyrow")
            .First.WaitForAsync(new() { Timeout = 10_000 });
    }

    public Task<bool> IsAuditHistoryDialogOpenAsync() => AuditHistoryDialog.IsVisibleAsync();

    public Task<int> GetAuditHistoryRowCountAsync() =>
        AuditHistoryDialog.Locator("[data-testid='document-audit-history-grid'] .e-row").CountAsync();

    public async Task ClickViewAuditHistoryRowAsync(string rowTextFragment)
    {
        var row = AuditHistoryDialog.Locator("[data-testid='document-audit-history-grid'] .e-row")
            .Filter(new() { HasText = rowTextFragment })
            .First;
        await row.GetByRole(AriaRole.Button, new() { Name = "View" }).ClickAsync();
        await AuditDetailDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsAuditDetailDialogOpenAsync() => AuditDetailDialog.IsVisibleAsync();

    public async Task<string> GetAuditDetailDialogTextAsync() =>
        (await AuditDetailDialog.InnerTextAsync()).Trim();

    public async Task CloseAuditDetailDialogAsync()
    {
        await AuditDetailDialog.Locator(".e-footer-content button:has-text('Close')").ClickAsync();
        await AuditDetailDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task CloseAuditHistoryDialogAsync()
    {
        await AuditHistoryDialog.Locator(".e-footer-content button:has-text('Close')").ClickAsync();
        await AuditHistoryDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }


    public async Task OpenEditAcknowledgementDialogAsync()
    {
        await AcknowledgementCard.GetByRole(AriaRole.Button, new() { Name = "Edit acknowledgement settings" }).ClickAsync();
        await EditAcknowledgementDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task WaitForOverlayToClearAsync()
    {
        await Assertions.Expect(page.Locator(".e-dlg-overlay:visible")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
        await Assertions.Expect(page.Locator(".e-dlg-container:visible")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }

    public Task<bool> IsEditAcknowledgementDialogOpenAsync() => EditAcknowledgementDialog.IsVisibleAsync();

    private ILocator AcknowledgementStatementTextArea => EditAcknowledgementDialog.Locator("textarea");

    public Task<string> GetAcknowledgementStatementValueAsync() =>
        AcknowledgementStatementTextArea.InputValueAsync();

    public async Task FillAcknowledgementStatementAsync(string value)
    {
        await AcknowledgementStatementTextArea.FillAsync(value);

        var dueDateInput = EditAcknowledgementDialog.Locator(".e-date-wrapper input.e-input");
        await dueDateInput.ClickAsync();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (await AcknowledgementStatementTextArea.InputValueAsync() == value)
                return;

            await page.WaitForTimeoutAsync(250);
        }
    }

    public async Task<bool> IsAcknowledgementStatementReadOnlyAsync() =>
        await AcknowledgementStatementTextArea.GetAttributeAsync("readonly") is not null;

    private ILocator ResetAcknowledgementStatementButton =>
        EditAcknowledgementDialog.GetByRole(AriaRole.Button, new() { Name = "Reset to Default" });

    public Task<bool> IsResetAcknowledgementStatementButtonDisabledAsync() =>
        ResetAcknowledgementStatementButton.IsDisabledAsync();

    public async Task ClickResetAcknowledgementStatementToDefaultAsync()
    {
        var beforeValue = await AcknowledgementStatementTextArea.InputValueAsync();

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            try
            {
                await ResetAcknowledgementStatementButton.ClickAsync(new() { Timeout = 5_000 });
                break;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await page.WaitForTimeoutAsync(250);
            }
        }

        var valueDeadline = DateTime.UtcNow.AddSeconds(10);
        while (await AcknowledgementStatementTextArea.InputValueAsync() == beforeValue && DateTime.UtcNow < valueDeadline)
            await page.WaitForTimeoutAsync(100);
    }

    public Task<bool> IsAcknowledgementLockedNoteVisibleAsync() =>
        EditAcknowledgementDialog.GetByText("Locked after publishing").IsVisibleAsync();

    public async Task SaveEditAcknowledgementDialogAsync()
    {
        await EditAcknowledgementDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await EditAcknowledgementDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        await WaitForOverlayToClearAsync();

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task OpenPublishDialogAsync()
    {
        await PublishHeaderButton.ClickAsync();
        await PublishDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsPublishDialogOpenAsync() => PublishDialog.IsVisibleAsync();

    public async Task ClickPublishCancelAsync()
    {
        await PublishDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await PublishDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task OpenArchiveDialogAsync()
    {
        await ClickMoreActionsItemAsync("Archive");
        await ArchiveDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsArchiveDialogOpenAsync() => ArchiveDialog.IsVisibleAsync();

    public async Task FillArchiveReasonAsync(string reason)
    {
        await page.Locator("#archive-reason").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task ClickArchiveConfirmAsync() =>
        ArchiveDialog.GetByRole(AriaRole.Button, new() { Name = "Archive", Exact = true }).ClickAsync();

    public Task ClickArchiveCancelAsync() =>
        ArchiveDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

    public async Task<string?> GetArchiveErrorAsync()
    {
        var error = ArchiveDialog.Locator(".alert-danger");
        try
        {
            await error.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await error.InnerTextAsync()).Trim();
    }

    public async Task ArchiveAsync(string reason)
    {
        await OpenArchiveDialogAsync();
        await FillArchiveReasonAsync(reason);
        await ClickArchiveConfirmAsync();
        await WaitForArchiveDialogToCloseAsync();
    }

    public async Task WaitForArchiveDialogToCloseAsync()
    {
        await ArchiveDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task OpenExpireDialogAsync()
    {
        await ClickMoreActionsItemAsync("Mark Expired");
        await ExpireDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsExpireDialogOpenAsync() => ExpireDialog.IsVisibleAsync();

    public async Task<string> GetExpireDialogBodyTextAsync() =>
        (await ExpireDialog.Locator("p").First.InnerTextAsync()).Trim();

    public Task ClickExpireConfirmAsync() =>
        ExpireDialog.GetByRole(AriaRole.Button, new() { Name = "Mark Expired", Exact = true }).ClickAsync();

    public async Task ClickExpireCancelAsync()
    {
        await ExpireDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await ExpireDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

        await WaitForOverlayToClearAsync();
    }

    public async Task<string?> GetExpireErrorAsync()
    {
        var error = ExpireDialog.Locator(".alert-danger");
        try
        {
            await error.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await error.InnerTextAsync()).Trim();
    }

    public async Task ExpireAsync()
    {
        await OpenExpireDialogAsync();
        await ClickExpireConfirmAsync();
        await WaitForExpireDialogToCloseAsync();
    }

    public async Task WaitForExpireDialogToCloseAsync()
    {
        await ExpireDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task OpenReviewDialogAsync()
    {
        await ReviewHeaderButton.ClickAsync();
        await ReviewDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsReviewDialogOpenAsync() => ReviewDialog.IsVisibleAsync();

    /// <summary>
    /// Text of a labelled dt/dd metadata row inside the Complete Review dialog (e.g. "Title",
    /// "Category", "Next Review Date", "Review Frequency", "Review Owner"), or null when the row
    /// isn't rendered (Review Frequency and Review Owner are only rendered when set — same as the
    /// page's own Document Metadata card). Deliberately scoped to the dialog itself rather than a
    /// bare "dt:has-text(...) + dd" page-level locator, since SharedDocumentDetail.razor's own
    /// Document Metadata card renders dt/dd rows with these exact same labels underneath the
    /// dialog, which would otherwise resolve to two elements.
    /// </summary>
    public async Task<string?> GetReviewDialogMetadataRowAsync(string label)
    {
        var row = ReviewDialog.Locator($"dt:has-text('{label}') + dd");
        if (!await row.IsVisibleAsync()) return null;
        return (await row.InnerTextAsync()).Trim();
    }

    public async Task FillReviewNotesAsync(string notes)
    {
        await page.Locator("#review-notes").FillAsync(notes);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task ClickReviewConfirmAsync() =>
        ReviewDialog.GetByRole(AriaRole.Button, new() { Name = "Complete Review", Exact = true }).ClickAsync();

    public async Task ClickReviewCancelAsync()
    {
        await ReviewDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await ReviewDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task<string?> GetReviewValidationErrorAsync()
    {
        var error = ReviewDialog.Locator(".text-danger.small.mt-1");
        try
        {
            await error.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await error.InnerTextAsync()).Trim();
    }

    public async Task CompleteReviewAsync(string notes)
    {
        await OpenReviewDialogAsync();
        await FillReviewNotesAsync(notes);
        await ClickReviewConfirmAsync();
        await WaitForReviewDialogToCloseAsync();
    }

    public async Task WaitForReviewDialogToCloseAsync()
    {
        await ReviewDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task SetReviewRenewedFileAsync(string filePath)
    {
        await ReviewDialog.Locator("input[type='file']").SetInputFilesAsync(filePath);
        await ReviewDialog.Locator("[data-testid='review-renewed-file-selected']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    private ILocator ReviewReacknowledgementCheckbox => ReviewDialog.Locator(".e-checkbox-wrapper")
        .Filter(new() { HasText = "Requires employees to acknowledge this version again" });

    public Task<bool> IsReviewReacknowledgementCheckboxVisibleAsync() =>
        ReviewReacknowledgementCheckbox.WaitUntilVisibleAsync();

    public Task CheckReviewReacknowledgementAsync() => ReviewReacknowledgementCheckbox.Locator("label").ClickAsync();

    public async Task CompleteReviewWithRenewedFileAsync(string notes, string filePath, bool requiresReacknowledgement = false)
    {
        await OpenReviewDialogAsync();
        await FillReviewNotesAsync(notes);
        await SetReviewRenewedFileAsync(filePath);

        if (requiresReacknowledgement)
        {
            await CheckReviewReacknowledgementAsync();
        }

        await ClickReviewConfirmAsync();
        await WaitForReviewDialogToCloseAsync();
    }

    public async Task OpenEditAudienceDialogAsync()
    {
        await AudienceCard.GetByRole(AriaRole.Button, new() { Name = "Edit audience" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Edit Document Audience" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task<string> GetAudienceSummaryAsync() =>
        (await page.Locator(".doc-detail-audience-summary").InnerTextAsync()).Trim();

    public Task<bool> IsDraftAudienceWarningVisibleAsync() =>
        page.Locator(".doc-detail-note-badge").IsVisibleAsync();

    public ILocator FooterSummary => page.Locator("p.text-muted.small.mb-0");

    public async Task<string> GetFooterSummaryTextAsync() =>
        (await FooterSummary.InnerTextAsync()).Trim();

    private ILocator VersionHistoryTab => page.GetByRole(AriaRole.Tab, new() { Name = "Version History" });
    private ILocator ReviewHistoryTab => page.GetByRole(AriaRole.Tab, new() { Name = "Review History" });
    private ILocator ActiveHistoryTabPane => page.Locator(".doc-detail-history-card .e-content > .e-item:visible").First;

    private async Task<ILocator> SelectVersionHistoryTabAsync()
    {
        await VersionHistoryTab.ClickAsync();
        await Assertions.Expect(VersionHistoryTab).ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 10_000 });
        return ActiveHistoryTabPane;
    }

    private async Task<ILocator> SelectReviewHistoryTabAsync()
    {
        await ReviewHistoryTab.ClickAsync();
        await Assertions.Expect(ReviewHistoryTab).ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 10_000 });
        return ActiveHistoryTabPane;
    }

    public async Task<int> GetVersionRowCountAsync()
    {
        var pane = await SelectVersionHistoryTabAsync();
        return await pane.Locator(".e-row").CountAsync();
    }

    public async Task<int> WaitForVersionRowCountAsync(int expectedCount)
    {
        var pane = await SelectVersionHistoryTabAsync();
        var rows = pane.Locator(".e-row");
        await Assertions.Expect(rows).ToHaveCountAsync(expectedCount, new() { Timeout = 15_000 });
        return await rows.CountAsync();
    }

    public async Task<IReadOnlyList<string>> GetVersionColumnHeadersAsync()
    {
        var pane = await SelectVersionHistoryTabAsync();
        await pane.Locator(".e-row, .e-emptyrow, .doc-detail-empty-state").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var headers = await pane.Locator(".e-grid .e-headercell").AllInnerTextsAsync();
        return headers.Select(h => h.Trim()).ToList();
    }

    public async Task<string> GetVersionRowCellAsync(string rowTextFragment, int columnIndex)
    {
        var pane = await SelectVersionHistoryTabAsync();
        var row = pane.Locator(".e-row").Filter(new() { HasText = rowTextFragment }).First;
        return (await row.Locator(".e-rowcell").Nth(columnIndex).InnerTextAsync()).Trim();
    }

    public async Task<(string Note, string RequiredAck, string EffectiveDate)> GetVersionDetailAsync(string rowTextFragment)
    {
        var pane = await SelectVersionHistoryTabAsync();
        var row = pane.Locator(".e-row").Filter(new() { HasText = rowTextFragment }).First;
        await row.Locator("button[aria-label^='Show details for version']").ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { NameRegex = new Regex("details", RegexOptions.IgnoreCase) });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var note = (await dialog.Locator("dt:has-text('Note') + dd").InnerTextAsync()).Trim();
        var requiredAck = (await dialog.Locator("dt:has-text('Required Ack') + dd").InnerTextAsync()).Trim();
        var effectiveDate = (await dialog.Locator("dt:has-text('Effective Date') + dd").InnerTextAsync()).Trim();

        await dialog.Locator(".e-footer-content button:has-text('Close')").ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

        return (note, requiredAck, effectiveDate);
    }

    public async Task<string?> GetVersionDownloadHrefAsync(string rowTextFragment)
    {
        var pane = await SelectVersionHistoryTabAsync();
        var row = pane.Locator(".e-row").Filter(new() { HasText = rowTextFragment }).First;
        return await row.Locator("a[title='Download this version']").GetAttributeAsync("href");
    }

    public async Task UploadNewVersionAsync(string versionNote, string filePath)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Upload New Version" }).ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Upload New Version" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.GetByPlaceholder("What changed in this version?").FillAsync(versionNote);
        await page.Keyboard.PressAsync("Tab");
        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public Task<bool> IsReviewHistoryCardVisibleAsync() => ReviewHistoryTab.WaitUntilVisibleAsync(15_000);

    public async Task<int> GetReviewHistoryRowCountAsync()
    {
        var pane = await SelectReviewHistoryTabAsync();
        await pane.Locator(".e-row, .e-emptyrow").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        return await pane.Locator(".e-row").CountAsync();
    }

    public async Task<int> WaitForReviewHistoryRowCountAsync(int expectedCount)
    {
        var pane = await SelectReviewHistoryTabAsync();
        var rows = pane.Locator(".e-row");
        await Assertions.Expect(rows).ToHaveCountAsync(expectedCount, new() { Timeout = 15_000 });
        return await rows.CountAsync();
    }

    public async Task<IReadOnlyList<string>> GetReviewHistoryColumnHeadersAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            var pane = await SelectReviewHistoryTabAsync();
            await pane.Locator(".e-row, .e-emptyrow").First
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
            var headers = (await pane.Locator(".e-headercell").AllInnerTextsAsync()).Select(h => h.Trim()).ToList();
            if (headers.Contains("Review Date") || DateTime.UtcNow > deadline)
                return headers;
            await page.WaitForTimeoutAsync(300);
        }
    }

    public async Task<string> GetReviewHistoryRowCellAsync(int rowIndex, int columnIndex)
    {
        var pane = await SelectReviewHistoryTabAsync();
        var row = pane.Locator(".e-row").Nth(rowIndex);
        return (await row.Locator(".e-rowcell").Nth(columnIndex).InnerTextAsync()).Trim();
    }

    /// <summary>
    /// Count of interactive controls (buttons, links, or icon glyphs) rendered anywhere inside the
    /// Review History grid's rows — expected to always be 0, since the grid is strictly read-only
    /// (no edit/delete/action column), unlike the Version History grid (which has per-row Download
    /// and Details controls). Deliberately scoped to just the grid's own rows (".e-row" inside the
    /// active pane), not the pane's "Record review" button that sits above the grid.
    /// </summary>
    public async Task<int> GetReviewHistoryRowActionControlCountAsync()
    {
        var pane = await SelectReviewHistoryTabAsync();
        return await pane.Locator(".e-row button, .e-row a, .e-row i").CountAsync();
    }

    // ── Ticket 2: optimistic-concurrency conflict on the metadata / audience / acknowledgement edit dialogs ──
    // Each of the three edit dialogs (EditSharedCompanyDocument{Metadata,Audience,Acknowledgement}Dialog.razor)
    // renders the shared <SaveConflictBanner> — a `div.alert.alert-warning.save-conflict-banner[role='alert']`
    // with a "Reload latest values" Syncfusion button — when its save is rejected with HTTP 409
    // (code=="concurrency"). The banner is matched by the component's own `.save-conflict-banner`
    // class scoped to the owning dialog, additionally filtered on the "Reload latest values" action
    // so an unrelated warning alert can never satisfy strict mode. Match on structure, not text.

    private ILocator ConflictBannerIn(ILocator dialog) =>
        page.Locator(".save-conflict-banner:visible");

    private async Task SaveDialogExpectingConflictAsync(ILocator dialog)
    {
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await ConflictBannerIn(dialog).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }

    private async Task SaveDialogExpectingSuccessAsync(ILocator dialog)
    {
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    private async Task ClickDialogReloadLatestAsync(ILocator dialog)
    {
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }).ClickAsync();
        await ConflictBannerIn(dialog).WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
    }


    public async Task OpenMetadataDialogAsync()
    {
        await EditMetadataHeaderButton.ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await Assertions.Expect(
                EditMetadataDialog.Locator(".col-md-6").Filter(new() { HasText = "Category" })
                    .Locator(".e-input-group input").First)
            .Not.ToHaveValueAsync("", new() { Timeout = 15_000 });
    }

    public Task SetMetadataTitleAsync(string value) =>
        ClearAndTypeAsync(EditMetadataDialog.GetByPlaceholder("Document title"), value);

    public Task<string> GetMetadataTitleValueAsync() =>
        EditMetadataDialog.GetByPlaceholder("Document title").InputValueAsync();

    public async Task WaitForMetadataTitleValueAsync(string expected) =>
        await Assertions.Expect(EditMetadataDialog.GetByPlaceholder("Document title"))
            .ToHaveValueAsync(expected, new() { Timeout = 15_000 });

    public Task<bool> IsMetadataDialogOpenAsync() => EditMetadataDialog.IsVisibleAsync();

    public async Task CloseMetadataDialogAsync()
    {
        await EditMetadataDialog.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
        await EditMetadataDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        await WaitForOverlayToClearAsync();
    }

    public Task<bool> IsMetadataConflictBannerVisibleAsync() => ConflictBannerIn(EditMetadataDialog).IsVisibleAsync();
    public Task SaveMetadataDialogExpectingConflictAsync() => SaveDialogExpectingConflictAsync(EditMetadataDialog);
    public Task SaveMetadataDialogExpectingSuccessAsync() => SaveDialogExpectingSuccessAsync(EditMetadataDialog);

    public async Task ClickMetadataReloadLatestAsync()
    {
        await ClickDialogReloadLatestAsync(EditMetadataDialog);
        await page.WaitForTimeoutAsync(300);
    }

    public async Task ChangeMetadataTitleAsync(string newTitle)
    {
        await OpenMetadataDialogAsync();
        await SetMetadataTitleAsync(newTitle);
        await SaveMetadataDialogExpectingSuccessAsync();
        await Assertions.Expect(page.Locator("h1").First).ToHaveTextAsync(newTitle, new() { Timeout = 15_000 });
    }


    private ILocator EditAudienceDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Edit Document Audience" });

    public async Task OpenAudienceDialogAsync()
    {
        await OpenEditAudienceDialogAsync();
        await EditAudienceDialog.GetByPlaceholder("Any department")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public Task<bool> IsAudienceDialogOpenAsync() => EditAudienceDialog.IsVisibleAsync();

    public async Task CloseAudienceDialogAsync()
    {
        await EditAudienceDialog.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
        await EditAudienceDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        await WaitForOverlayToClearAsync();
    }

    public Task<bool> IsAudienceConflictBannerVisibleAsync() => ConflictBannerIn(EditAudienceDialog).IsVisibleAsync();
    public Task SaveAudienceDialogExpectingConflictAsync() => SaveDialogExpectingConflictAsync(EditAudienceDialog);
    public Task SaveAudienceDialogExpectingSuccessAsync() => SaveDialogExpectingSuccessAsync(EditAudienceDialog);

    public async Task ClickAudienceReloadLatestAsync()
    {
        await ClickDialogReloadLatestAsync(EditAudienceDialog);
        await EditAudienceDialog.GetByPlaceholder("Any department")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await page.WaitForTimeoutAsync(300);
    }


    public Task<bool> IsAcknowledgementDialogOpenAsync() => EditAcknowledgementDialog.IsVisibleAsync();
    public Task<bool> IsAcknowledgementConflictBannerVisibleAsync() => ConflictBannerIn(EditAcknowledgementDialog).IsVisibleAsync();
    public Task SaveAcknowledgementDialogExpectingConflictAsync() => SaveDialogExpectingConflictAsync(EditAcknowledgementDialog);
    public Task SaveAcknowledgementDialogExpectingSuccessAsync() => SaveDialogExpectingSuccessAsync(EditAcknowledgementDialog);

    public async Task ClickAcknowledgementReloadLatestAsync()
    {
        await ClickDialogReloadLatestAsync(EditAcknowledgementDialog);
        await page.WaitForTimeoutAsync(300);
    }
}
