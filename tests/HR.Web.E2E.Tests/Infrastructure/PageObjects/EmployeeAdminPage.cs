using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class EmployeeAdminPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}");
        await page.WaitForSelectorAsync(".content-area span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task<string> GetActiveTabNameAsync()
    {
        var section = page.Locator(".employee-profile-sections [role='tab'][aria-selected='true']").First;
        if (await section.CountAsync() > 0)
        {
            await section.WaitForAsync(new() { Timeout = 10_000 });
            return (await section.TextContentAsync())?.Trim() ?? "";
        }

        var active = page.Locator("[role='tab'][aria-selected='true']").First;
        await active.WaitForAsync(new() { Timeout = 10_000 });
        return (await active.TextContentAsync())?.Trim() ?? "";
    }


    public async Task OpenDocumentsTabAsync()
    {
        await EmployeeEditPage.NavigateToSectionAsync(page, "Documents");
        await page.WaitForSpinnerToClearAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='employee-documents-grid-section'] .card-header",
            new() { Timeout = 15_000 });
    }

    public async Task<bool> HasDocumentAsync(string titleFragment)
    {
        try
        {
            await page.Locator("[data-testid='employee-documents-grid-section'] .e-grid .e-row")
                .Filter(new() { HasText = titleFragment })
                .First
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }


    private ILocator UploadDocumentDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });

    public async Task OpenUploadDocumentDialogAsync()
    {
        await page.Locator("[data-testid='employee-documents-grid-section']")
            .GetByRole(AriaRole.Button, new() { Name = "Upload" })
            .ClickAsync();
        await UploadDocumentDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> HasUploadDialogDocumentDetailsTabAsync() =>
        UploadDocumentDialog.GetByRole(AriaRole.Tab, new() { Name = "Document Details" }).IsVisibleAsync();

    public Task<bool> HasUploadDialogFileTabAsync() =>
        UploadDocumentDialog.GetByRole(AriaRole.Tab, new() { Name = "File" }).IsVisibleAsync();

    public Task<bool> IsUploadDialogFileInputVisibleAsync() =>
        UploadDocumentDialog.Locator("input[type='file']").IsVisibleAsync();

    public async Task FillUploadDialogTitleAsync(string value)
    {
        await UploadDocumentDialog.GetByPlaceholder("Document title").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task SelectUploadDialogDocumentTypeAsync(string typeNameFragment) =>
        DropDownSelector.SelectAsync(page, UploadDocumentDialog, typeNameFragment);

    public async Task SelectUploadDialogFileAsync(string filePath)
    {
        await UploadDocumentDialog.GetByRole(AriaRole.Tab, new() { Name = "File" }).ClickAsync();
        await UploadDocumentDialog.Locator("input[type='file']").SetInputFilesAsync(filePath);
    }

    public async Task SubmitUploadDocumentDialogAsync()
    {
        await UploadDocumentDialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await UploadDocumentDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public async Task<bool> HasDocumentRequestsSectionAsync() =>
        await page.Locator("[data-testid='admin-document-requests-section']").IsVisibleAsync();

    public async Task<bool> HasDocumentRequestAsync(string documentTypeName)
    {
        try
        {
            await page.Locator("[data-testid='admin-document-requests-section'] td")
                .Filter(new() { HasText = documentTypeName })
                .First
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string?> GetDocumentRequestStatusAsync(string documentTypeName)
    {
        var row = page.Locator("[data-testid='admin-document-requests-section'] tr")
            .Filter(new() { HasText = documentTypeName })
            .First;
        var badge = row.Locator(".badge");
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    public async Task<bool> HasRequestDocumentButtonAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Request Document" }).IsVisibleAsync();

    public async Task RequestDocumentAsync(string documentTypeName, DateOnly? dueDate = null)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Request Document" }).ClickAsync();

        await page.WaitForSelectorAsync(".request-document-dialog", new() { Timeout = 10_000 });

        await DropDownSelector.SelectAsync(page, page.Locator(".request-document-dialog"), documentTypeName);

        if (dueDate.HasValue)
        {
            await page.Locator(".request-document-dialog input.e-datepicker").FillAsync(dueDate.Value.ToString("dd/MM/yyyy"));
            await page.Keyboard.PressAsync("Tab");
        }

        await page.Locator(".request-document-dialog").GetByRole(AriaRole.Button, new() { Name = "Request" }).ClickAsync();

        await page.WaitForFunctionAsync(
            "!document.querySelector('.request-document-dialog') || !document.querySelector('.request-document-dialog').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        await page.WaitForSelectorAsync("[data-testid='admin-document-requests-section']", new() { Timeout = 10_000 });
    }

    public async Task OpenRequestDocumentDialogSelectTypeThenCancelAsync(string documentTypeName)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Request Document" }).ClickAsync();
        await page.WaitForSelectorAsync(".request-document-dialog", new() { Timeout = 10_000 });

        await DropDownSelector.SelectAsync(page, page.Locator(".request-document-dialog"), documentTypeName);

        await page.Locator(".request-document-dialog").GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

        var unsavedDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Unsaved Changes" });
        await unsavedDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await unsavedDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();

        await page.WaitForFunctionAsync(
            "!document.querySelector('.request-document-dialog') || !document.querySelector('.request-document-dialog').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }


    public async Task OpenAssetsTabAsync()
    {
        await EmployeeEditPage.NavigateToSectionAsync(page, "Assets");
        await page.WaitForSpinnerToClearAsync();
        await page.WaitForSelectorAsync("[data-testid='employee-assets-grid'], .text-muted",
            new() { Timeout = 15_000 });

        if (await page.Locator("[data-testid='employee-assets-grid']").IsVisibleAsync())
        {
            await page.WaitForSelectorAsync(
                "[data-testid='employee-assets-grid'] .e-row, [data-testid='employee-assets-grid'] .e-emptyrow",
                new() { Timeout = 15_000 });
        }
    }

    public async Task<bool> HasAssetsGridRowsAsync() =>
        await page.Locator("[data-testid='employee-assets-grid'] .e-row").CountAsync() > 0;

    public async Task<IReadOnlyList<string>> GetAssetsGridAssetNumbersAsync()
    {
        var spans = await page
            .Locator("[data-testid='employee-assets-grid'] .e-row .e-rowcell:first-child .fw-medium")
            .AllTextContentsAsync();
        return spans.Select(t => t.Trim()).ToList();
    }

    public async Task<bool> HasAssignAssetButtonAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Assign Asset" }).IsVisibleAsync();

    public async Task<bool> HasReturnAssetButtonAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Return Asset" }).IsVisibleAsync();

    public async Task<bool> IsReturnAssetButtonDisabledAsync()
    {
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Return Asset" });
        return await btn.IsDisabledAsync();
    }

    public async Task OpenAssignAssetDialogAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Assign Asset" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Assign Asset" })
            .WaitForAsync(new() { Timeout = 15_000 });
    }

    public async Task<bool> IsAssignAssetDialogVisibleAsync() =>
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Assign Asset" }).IsVisibleAsync();

    public async Task SelectAssetAndConfirmAsync(string assetFragment)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Assign Asset" });
        await DropDownSelector.SelectAsync(page, dialog, assetFragment);
        await page.GetByRole(AriaRole.Button, new() { Name = "Assign", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Assign Asset" })
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public async Task CloseAssignAssetDialogAsync()
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Assign Asset" });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task OpenReturnAssetDialogAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Return Asset" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Request Return" })
            .WaitForAsync(new() { Timeout = 15_000 });
    }

    public async Task SelectAssetAndConfirmReturnAsync(string assetFragment)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Request Return" });
        await DropDownSelector.SelectAsync(page, dialog, assetFragment);
        await page.GetByRole(AriaRole.Button, new() { Name = "Request Return", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Request Return" })
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }


    public async Task OpenLeaveTabAsync()
    {
        await EmployeeEditPage.NavigateToSectionAsync(page, "Leave");
        await page.WaitForSpinnerToClearAsync();
        await page.Locator(".card-body .fs-4.fw-semibold").First.WaitForAsync(new() { Timeout = 15_000 });
    }

    public async Task<string?> GetBalanceRowTextAsync(string leaveTypeName)
    {
        var row = page.Locator(".col-md-4").Filter(new() { HasText = leaveTypeName }).First;
        var value = row.Locator(".fs-4.fw-semibold");
        await value.WaitForAsync(new() { Timeout = 15_000 });
        return (await value.TextContentAsync())?.Trim();
    }

    public async Task<bool> HasAdjustButtonAsync(string leaveTypeName)
    {
        var row = page.Locator(".col-md-4").Filter(new() { HasText = leaveTypeName }).First;
        return await row.GetByRole(AriaRole.Button, new() { Name = "Adjust" }).IsVisibleAsync();
    }

    public async Task<string?> GetToilBalanceTextAsync()
    {
        var card = page.Locator(".card").Filter(new() { HasText = "TOIL Balance" }).First;
        var value = card.Locator(".fs-4.fw-semibold");
        await value.WaitForAsync(new() { Timeout = 15_000 });
        return (await value.TextContentAsync())?.Trim();
    }

    public async Task<bool> HasToilAdjustButtonAsync()
    {
        var card = page.Locator(".card").Filter(new() { HasText = "TOIL Balance" }).First;
        return await card.GetByRole(AriaRole.Button, new() { Name = "Adjust" }).IsVisibleAsync();
    }

    public async Task OpenAdjustDialogAsync(string leaveTypeName)
    {
        var row = page.Locator(".col-md-4").Filter(new() { HasText = leaveTypeName }).First;
        await row.GetByRole(AriaRole.Button, new() { Name = "Adjust" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {leaveTypeName} Balance" })
            .WaitForAsync(new() { Timeout = 10_000 });
    }

    public async Task OpenToilAdjustDialogAsync(string toilLeaveTypeName = "Time Off In Lieu")
    {
        var card = page.Locator(".card").Filter(new() { HasText = "TOIL Balance" }).First;
        await card.GetByRole(AriaRole.Button, new() { Name = "Adjust" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {toilLeaveTypeName} Balance" })
            .WaitForAsync(new() { Timeout = 10_000 });
    }

    public async Task<bool> IsAdjustDialogVisibleAsync(string leaveTypeName) =>
        await page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {leaveTypeName} Balance" }).IsVisibleAsync();

    public Task FillAdjustmentAmountAsync(string leaveTypeName, decimal hours) =>
        FillAdjustmentFormAsync(
            page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {leaveTypeName} Balance" }),
            hours, reason: null, comments: null, allowNegativeOverride: false);

    public async Task SubmitAdjustmentAsync(
        string leaveTypeName,
        decimal? hours,
        string? reason = null,
        string? comments = null,
        bool allowNegativeOverride = false)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {leaveTypeName} Balance" });
        await FillAdjustmentFormAsync(dialog, hours, reason, comments, allowNegativeOverride);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        try
        {
            await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            await dialog.Locator(".alert-danger")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8_000 });
        }
    }

    private async Task FillAdjustmentFormAsync(
        ILocator dialog, decimal? hours, string? reason, string? comments, bool allowNegativeOverride)
    {
        if (hours.HasValue)
        {
            var hoursInput = dialog.Locator("input.e-numerictextbox");
            await hoursInput.ClickAsync();
            await page.Keyboard.PressAsync("Control+A");
            await page.Keyboard.PressAsync("Delete");
            await hoursInput.PressSequentiallyAsync(hours.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await page.Keyboard.PressAsync("Tab");
        }

        if (!string.IsNullOrEmpty(reason))
        {
            await DropDownSelector.SelectAsync(page, dialog, reason);
        }

        if (!string.IsNullOrEmpty(comments))
        {
            await dialog.Locator("textarea").FillAsync(comments);
            await page.Keyboard.PressAsync("Tab");
        }

        if (allowNegativeOverride)
            await dialog.Locator("#allowNegativeOverride").CheckAsync();
    }

    public async Task CloseAdjustDialogAsync(string leaveTypeName)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {leaveTypeName} Balance" });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task<string?> GetAdjustDialogErrorAsync(string leaveTypeName)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {leaveTypeName} Balance" });
        var error = dialog.Locator(".alert-danger");
        return await error.IsVisibleAsync() ? (await error.TextContentAsync())?.Trim() : null;
    }

    public async Task<string?> GetAdjustmentLabelTextAsync(string leaveTypeName)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {leaveTypeName} Balance" });
        var label = dialog.Locator("label.form-label").Filter(new() { HasText = "Adjustment (" });
        await label.WaitForAsync(new() { Timeout = 10_000 });
        return (await label.TextContentAsync())?.Trim();
    }

    public async Task<string?> GetAdjustDialogCurrentBalanceTextAsync(string leaveTypeName)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Adjust {leaveTypeName} Balance" });
        var input = dialog.Locator("input.form-control[readonly]");
        return (await input.InputValueAsync())?.Trim();
    }


    public async Task OpenEmploymentTabAsync()
    {
        await EmployeeEditPage.NavigateToSectionAsync(page, "Employment");
        await page.WaitForSelectorAsync(".card-header:has-text('Employment Details')", new() { Timeout = 15_000 });
    }


    public async Task EnableWorkingPatternOverrideAsync()
    {
        var numericInput = page.Locator("input.e-numerictextbox");
        if (!await numericInput.IsVisibleAsync())
        {
            var wrapper = page.Locator(".e-checkbox-wrapper")
                .Filter(new() { HasText = "Use company working pattern" });
            await wrapper.Locator("label").ClickAsync();
            await numericInput.WaitForAsync(new() { Timeout = 10_000 });
        }
    }

    public async Task SetHoursPerDayAsync(decimal hours)
    {
        var input = page.Locator("input.e-numerictextbox").First;
        await input.FillAsync(hours.ToString("0.#"));
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForURLAsync("**/employees/*/view", new() { Timeout = 15_000 });
    }
}
