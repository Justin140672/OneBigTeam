using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class MyProfilePage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}/profile");
        await page.EvaluateAsync(@"() => {
            window._profileReady = false;
            window._profileError = null;
            const poll = setInterval(() => {
                if (!document.querySelector('.overview-skeleton') &&
                    document.querySelector('.overview-grid')) {
                    window._profileReady = true;
                    clearInterval(poll);
                } else if (!document.querySelector('.overview-skeleton')) {
                    const err = Array.from(document.querySelectorAll('.alert-danger, .alert-warning'))
                        .find(a => /You can only view your own profile|Unable to load profile details/.test(a.textContent));
                    if (err) {
                        window._profileError = err.textContent.trim();
                        clearInterval(poll);
                    }
                }
            }, 500);
        }");
        try
        {
            await page.WaitForFunctionAsync(
                "window._profileReady === true || window._profileError !== null",
                null, new PageWaitForFunctionOptions { Timeout = 60_000 });
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"MyProfilePage.GoToAsync timed out waiting for '.overview-grid' " +
                $"(company={companyId}, employee={employeeId}).", ex);
        }

        var error = await page.EvaluateAsync<string?>("window._profileError");
        if (error is not null)
            throw new InvalidOperationException($"MyProfilePage.GoToAsync: profile rendered an error state instead of the overview grid: \"{error}\"");
    }

    public async Task WaitForLoadAsync()
    {
        await page.WaitForSelectorAsync(".e-tab", new() { Timeout = 20_000 });
    }

    public async Task<string> GetActiveTabNameAsync()
    {
        var active = page.Locator("[role='tab'][aria-selected='true']").First;
        await active.WaitForAsync(new() { Timeout = 10_000 });
        return (await active.TextContentAsync())?.Trim() ?? "";
    }


    public async Task UploadMyProfilePhotoAsync(string filePath)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Change Photo" }).ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Change Profile Photo" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();

        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public Task<bool> HasPendingProfilePhotoBannerAsync() =>
        page.Locator(".alert-warning").Filter(new() { HasText = "Pending approval" }).First.WaitUntilVisibleAsync(15_000);


    public async Task OpenOverviewTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Overview" }).ClickAsync();
        await page.WaitForSelectorAsync(".overview-grid, .overview-skeleton, .alert",
            new() { Timeout = 15_000 });
    }

    public async Task OpenContactDetailsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Contact Details" }).ClickAsync();
        await page.WaitForSelectorAsync(".cd-card, .alert", new() { Timeout = 15_000 });
    }

    public async Task OpenPersonalDetailsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Personal Details" }).ClickAsync();
        await page.WaitForSelectorAsync(".pd-card, .alert", new() { Timeout = 15_000 });
    }

    public async Task OpenEmergencyContactsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Emergency Contacts" }).ClickAsync();
        await page.WaitForSelectorAsync(".ec-card, .alert", new() { Timeout = 15_000 });
    }


    public async Task OpenEqualityDiversityTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Equality & Diversity" }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='my-profile-equality-section'], .ed-section, .alert-danger",
            new() { Timeout = 15_000 });
    }

    public async Task OpenEqualityGenderAsync(string optionText) =>
        await DropDownSelector.SelectAsync(page, page.Locator("[data-testid='my-profile-equality-gender']"), optionText);

    public async Task OpenEqualityMaritalAsync(string optionText) =>
        await DropDownSelector.SelectAsync(page, page.Locator("[data-testid='my-profile-equality-marital']"), optionText);

    public async Task OpenEqualityEthnicGroupAsync(string optionText) =>
        await DropDownSelector.SelectAsync(page, page.Locator("[data-testid='my-profile-equality-ethnicgroup']"), optionText);

    public async Task OpenEqualityDisabilityAsync(string optionText) =>
        await DropDownSelector.SelectAsync(page, page.Locator("[data-testid='my-profile-equality-disability']"), optionText);

    public async Task OpenEqualityOrientationAsync(string optionText) =>
        await DropDownSelector.SelectAsync(page, page.Locator("[data-testid='my-profile-equality-orientation']"), optionText);

    public async Task OpenEqualityReligionAsync(string optionText) =>
        await DropDownSelector.SelectAsync(page, page.Locator("[data-testid='my-profile-equality-religion']"), optionText);

    public async Task<string> GetEqualitySelectedValueAsync(string fieldTestId) =>
        (await page.Locator($"[data-testid='{fieldTestId}'] span[role='combobox'] input").First.InputValueAsync())?.Trim() ?? "";

    public async Task SaveEqualityAsync()
    {
        await page.Locator("[data-testid='my-profile-equality-save']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='my-profile-equality-success']", new() { Timeout = 15_000 });
    }

    public async Task ClearEqualityAnswersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear my answers" }).ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='my-profile-equality-success']", new() { Timeout = 15_000 });
    }

    public async Task<bool> IsEqualitySuccessBannerVisibleAsync() =>
        await page.Locator("[data-testid='my-profile-equality-success']").IsVisibleAsync();

    public async Task OpenDocumentsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Documents", Exact = true }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='my-profile-documents-grid-section'] .e-grid .e-row, " +
            "[data-testid='my-profile-documents-grid-section'] .e-grid .e-emptyrow",
            new() { Timeout = 15_000 });
    }

    public Task<bool> HasUploadButtonForDocumentRequestAsync(string documentTypeName) =>
        DocumentRequestRow(documentTypeName).GetByRole(AriaRole.Button, new() { Name = "Upload" }).IsVisibleAsync();

    public async Task UploadRequestedDocumentAsync(string documentTypeName, string filePath)
    {
        await DocumentRequestRow(documentTypeName)
            .GetByRole(AriaRole.Button, new() { Name = "Upload" })
            .ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Upload {documentTypeName}" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();

        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    private ILocator DocumentRequestRow(string documentTypeName) =>
        page.Locator("[data-testid='my-profile-document-requests-section'] tbody tr")
            .Filter(new() { HasText = documentTypeName })
            .First;


    private ILocator DocumentRow(string title) =>
        page.Locator("[data-testid='my-profile-documents-grid-section'] .e-grid .e-row")
            .Filter(new() { HasText = title })
            .First;

    public async Task<bool> HasDocumentRowAsync(string title) =>
        await DocumentRow(title).CountAsync() > 0 && await DocumentRow(title).IsVisibleAsync();

    public async Task<string> GetDocumentRowSourceAsync(string title)
    {
        var row = DocumentRow(title);
        await row.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var badge = row.Locator(".badge").First;
        await badge.WaitForAsync(new() { Timeout = 15_000 });
        return (await badge.InnerTextAsync()).Trim();
    }

    public async Task<string?> GetDocumentRowStatusTextAsync(string title)
    {
        var row = DocumentRow(title);
        await row.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var badges = row.Locator(".badge");
        await badges.Last.WaitForAsync(new() { Timeout = 15_000 });
        var count = await badges.CountAsync();
        return count >= 2 ? (await badges.Nth(count - 1).InnerTextAsync()).Trim() : null;
    }

    public async Task<IReadOnlyList<string>> GetDocumentsGridColumnHeadersAsync()
    {
        var headerCell = page.Locator("[data-testid='my-profile-documents-grid-section'] .e-grid .e-headercell");
        await headerCell.First.WaitForAsync(new() { Timeout = 15_000 });

        var headers = await headerCell.AllAsync();
        var result = new List<string>();
        foreach (var header in headers)
            result.Add((await header.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    public Task ClickDocumentTitleLinkAsync(string title) =>
        DocumentRow(title).Locator("a").Filter(new() { HasText = title }).First.ClickAsync();

    public Task ClickDocumentRowActionAsync(string title, string actionName) =>
        DocumentRow(title).GetByRole(AriaRole.Button, new() { Name = actionName }).ClickAsync();

    public async Task OpenTasksTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Tasks" }).ClickAsync();

        await page.WaitForSelectorAsync(
            ".e-tab .e-item.e-active .e-grid .e-row, .e-tab .e-item.e-active .e-grid .e-emptyrow",
            new() { Timeout = 15_000 });
    }

    public async Task<IReadOnlyList<string>> GetTaskTitlesAsync()
    {
        var titles = await page.Locator(".task-title").AllAsync();
        var names  = new List<string>();
        foreach (var t in titles)
            names.Add((await t.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    public async Task ClickTaskAsync(string titleFragment)
    {
        var row = page.Locator(".task-title").Filter(new() { HasText = titleFragment }).First;
        await row.WaitForAsync(new() { Timeout = 15_000 });
        await row.ClickAsync();
        await page.WaitForSelectorAsync("[role='dialog'].task-view-dialog", new() { Timeout = 15_000 });
    }

    public async Task<string> GetTaskStatusAsync(string titleFragment)
    {
        var row = page.Locator(".e-row").Filter(new() { HasText = titleFragment }).First;
        var badge = row.Locator(".task-status-badge");
        await badge.WaitForAsync(new() { Timeout = 15_000 });
        return (await badge.InnerTextAsync()).Trim();
    }

    public async Task OpenAssetsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Assets" }).ClickAsync();
        await page.WaitForSelectorAsync(".e-grid, .spinner-border", new() { Timeout = 15_000 });
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        if (await page.Locator(".e-grid").First.IsVisibleAsync())
        {
            await page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow",
                new() { Timeout = 15_000 });
        }
    }

    public async Task<bool> HasAssetsTableAsync() =>
        await page.Locator(".e-grid .e-row").CountAsync() > 0;

    public async Task<IReadOnlyList<string>> GetAssetNumbersAsync()
    {
        var spans = await page.Locator(".e-grid .e-row .e-rowcell:first-child .fw-medium").AllTextContentsAsync();
        return spans.Select(t => t.Trim()).ToList();
    }

    public async Task<int> GetAssetRowCountAsync() =>
        await page.Locator(".e-grid .e-row").CountAsync();


    public async Task OpenLeaveTabAsync()
    {
        var leaveTab = page.GetByRole(AriaRole.Tab, new() { Name = "Leave" });
        await leaveTab.ClickAsync();
        await page.Locator(".card").Filter(new() { HasText = "Annual Leave" })
            .Locator("dd.fs-4.fw-semibold")
            .WaitForAsync(new() { Timeout = 20_000 });
    }

    public async Task<decimal?> GetAnnualLeaveRemainingAsync()
    {
        var card = page.Locator(".card").Filter(new() { HasText = "Annual Leave" }).First;
        var dd   = card.Locator("dd.fs-4.fw-semibold");
        await dd.WaitForAsync(new() { Timeout = 20_000 });
        var text = (await dd.TextContentAsync())?.Trim() ?? "";
        var match = System.Text.RegularExpressions.Regex.Match(text, @"[\d.]+");
        return match.Success && decimal.TryParse(match.Value, out var v) ? v : null;
    }

    public async Task<string?> GetAnnualLeaveRemainingTextAsync()
    {
        var card = page.Locator(".card").Filter(new() { HasText = "Annual Leave" }).First;
        var dd = card.Locator("dd.fs-4.fw-semibold");
        await dd.WaitForAsync(new() { Timeout = 20_000 });
        return (await dd.TextContentAsync())?.Trim();
    }

    public async Task<bool> HasAnyAdjustButtonOnLeaveTabAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Adjust" }).CountAsync() > 0;

    public async Task ClickRequestLeaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Request Leave" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" })
            .WaitForAsync(new() { Timeout = 10_000 });
    }

    /// <summary>
    /// Returns the status badge text for the leave request row whose text contains
    /// <paramref name="reasonFragment"/>. When a request is rejected the Reason column
    /// switches to the rejection reason, so pass <paramref name="altFragment"/> as the
    /// rejection reason to find the row in that case.
    /// </summary>
    public async Task<string?> GetLeaveRequestStatusAsync(string reasonFragment, string? altFragment = null)
    {
        var row = page.Locator("table tbody tr")
            .Filter(new() { HasText = reasonFragment })
            .First;

        try
        {
            await row.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException) when (altFragment is not null)
        {
        }

        if (!await row.IsVisibleAsync() && altFragment is not null)
        {
            row = page.Locator("table tbody tr")
                .Filter(new() { HasText = altFragment })
                .First;
            try
            {
                await row.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            }
            catch (TimeoutException)
            {
            }
        }

        if (!await row.IsVisibleAsync()) return null;

        var statusCell = row.Locator(".badge");
        return (await statusCell.TextContentAsync())?.Trim();
    }

    public async Task<string?> GetLeaveRequestRejectionReasonAsync(string reasonFragment)
    {
        var row = page.Locator("table tbody tr")
            .Filter(new() { HasText = reasonFragment })
            .First;

        var cells = await row.Locator("td").AllAsync();
        if (cells.Count < 6) return null;
        return (await cells[5].TextContentAsync())?.Trim();
    }


    public async Task FillLeaveRequestAsync(
        string leaveTypeName,
        string startDate,
        string endDate,
        string? reason = null)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" });

        await DropDownSelector.SelectAsync(page, dialog, leaveTypeName);

        var dateInputs = dialog.Locator(".e-date-wrapper input.e-input");
        await dateInputs.Nth(0).ClickAsync();
        await dateInputs.Nth(0).FillAsync(startDate);
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(dateInputs.Nth(0)).ToHaveValueAsync(startDate);
        await page.WaitForTimeoutAsync(300);

        await dateInputs.Nth(1).ClickAsync();
        await dateInputs.Nth(1).FillAsync(endDate);
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(dateInputs.Nth(1)).ToHaveValueAsync(endDate);
        await page.WaitForTimeoutAsync(300);

        if (!string.IsNullOrEmpty(reason))
        {
            var reasonInput = dialog.GetByPlaceholder("Reason for leave request");
            await reasonInput.FillAsync(reason);
            await page.Keyboard.PressAsync("Tab");
            await Assertions.Expect(reasonInput).ToHaveValueAsync(reason);
            await page.WaitForTimeoutAsync(300);
        }
    }

    public async Task SubmitLeaveRequestAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Submit Request" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Request Leave" })
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }
}
