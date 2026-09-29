using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class TaskViewPage(IPage page, string baseUrl)
{
    private ILocator Dialog => page.Locator("[role='dialog'].task-view-dialog");

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    public async Task GoToAsync(Guid companyId, Guid employeeId, Guid taskId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}/profile?tab=tasks");
        await WaitForTaskListLoadedAsync();

        var row = page.Locator($"[data-testid='task-view-btn-{taskId}']");
        await row.WaitForAsync(new() { Timeout = 20_000 });
        await row.ClickAsync();

        await WaitForLoadedAsync();
    }

    public async Task GoToByTitleAsync(Guid companyId, Guid employeeId, string taskTitle)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}/profile?tab=tasks");
        await WaitForTaskListLoadedAsync();

        var row = page.GetByRole(AriaRole.Button, new() { Name = taskTitle, Exact = true });
        await row.WaitForAsync(new() { Timeout = 20_000 });
        await row.ClickAsync();

        await WaitForLoadedAsync();
    }

    private async Task WaitForTaskListLoadedAsync()
    {
        try
        {
            await page.Locator(".hr-loading").WaitForAsync(
                new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
        }
        catch (TimeoutException)
        {
        }
    }

    public async Task WaitForLoadedAsync()
    {
        await Dialog.WaitForAsync(new() { Timeout = 15_000 });
        await page.WaitForFunctionAsync(
            "document.querySelector('.task-view-dialog [data-testid=\"task-title\"]')?.textContent?.trim() !== 'Task'",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        await page.WaitForSelectorAsync(
            ".task-view-dialog [data-testid$='-panel'], .task-view-dialog [data-testid='complete-task-btn'], .task-view-dialog .card-header",
            new() { Timeout = 15_000 });
    }

    public async Task<string> GetTitleAsync() =>
        (await Dialog.Locator("[data-testid='task-title']").TextContentAsync())?.Trim() ?? "";

    public async Task<string?> GetDescriptionAsync()
    {
        var p = Dialog.Locator(".card-body p.mb-3").First;
        return await p.IsVisibleAsync() ? (await p.TextContentAsync())?.Trim() : null;
    }

    public async Task<string?> GetDetailAsync(string label)
    {
        var dt = Dialog.Locator("dl.row dt").Filter(new() { HasText = label }).First;
        try
        {
            await dt.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        return (await dt.Locator("~ dd").First.TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns "Completed" if the (still-open) dialog's detail list shows a "Completed" row,
    /// otherwise "Not Started". NOTE: completing a task now CLOSES the dialog (see
    /// TaskViewDialog.OnActionCompletedAsync), so after a Complete*/Confirm* call read the list
    /// row instead — <see cref="GetListTaskStatusAsync"/> / <see cref="GetListTaskStatusByTitleAsync"/>.
    /// </summary>
    public async Task<string> GetStatusAsync()
    {
        // A successful terminal action (Complete/Confirm/Approve/Reject/…) closes the dialog — every
        // such method here waits for that close before returning — so a dialog that is no longer
        // visible at this point can only mean the task was just actioned: report "Completed".
        if (!await Dialog.IsVisibleAsync())
            return "Completed";

        var completedRow = Dialog.Locator("dl.row dt").Filter(new() { HasText = "Completed" });
        return await completedRow.IsVisibleAsync() ? "Completed" : "Not Started";
    }

    public async Task WaitForCompletedAsync() =>
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

    public async Task<string> GetListTaskStatusAsync(Guid taskId)
    {
        var badge = page.Locator($".e-row:has([data-testid='task-view-btn-{taskId}']) .task-status-badge").First;
        await badge.WaitForAsync(new() { Timeout = 15_000 });
        var cssClass = await badge.GetAttributeAsync("class") ?? "";
        return cssClass.Contains("task-status-badge--completed") ? "Completed" : "Not Started";
    }

    public async Task<string> GetListTaskStatusByTitleAsync(string taskTitle)
    {
        var badge = page.Locator(".e-row")
            .Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = taskTitle, Exact = true }) })
            .First.Locator(".task-status-badge").First;
        await badge.WaitForAsync(new() { Timeout = 15_000 });
        var cssClass = await badge.GetAttributeAsync("class") ?? "";
        return cssClass.Contains("task-status-badge--completed") ? "Completed" : "Not Started";
    }

    public async Task CloseAsync()
    {
        if (!await Dialog.IsVisibleAsync())
            return;

        await Dialog.GetByText("Close", new() { Exact = true }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }


    public async Task<bool> HasLeaveReviewPanelAsync() =>
        await Dialog.Locator(".card-header").Filter(new() { HasText = "Review Leave Request" }).IsVisibleAsync();

    public async Task EnterDecisionReasonAsync(string reason)
    {
        await Dialog.GetByPlaceholder("Enter a reason for your decision…").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task ApproveAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await WaitForCompletedAsync();
    }

    public async Task RejectAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Reject" }).ClickAsync();
        await WaitForCompletedAsync();
    }


    public async Task<bool> HasDocumentUploadPanelAsync() =>
        await Dialog.Locator("[data-testid='document-upload-panel']").IsVisibleAsync();

    public async Task SetDocumentTitleAsync(string title)
    {
        var panel = Dialog.Locator("[data-testid='document-upload-panel']");
        var input = panel.GetByPlaceholder("Document title");
        await input.ClearAsync();
        await input.FillAsync(title);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task AttachUploadFileAsync(string filePath)
    {
        var fileInput = Dialog.Locator("[data-testid='document-upload-panel'] input[type='file']");
        await fileInput.SetInputFilesAsync(filePath);
    }

    public async Task SubmitDocumentUploadAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();
        await WaitForCompletedAsync();
    }


    public async Task<bool> HasAssetAcknowledgementPanelAsync() =>
        await Dialog.Locator("[data-testid='asset-acknowledgement-panel']").IsVisibleAsync();

    public async Task<string?> GetAcknowledgementAssetNumberAsync()
    {
        var panel = Dialog.Locator("[data-testid='asset-acknowledgement-panel']");
        var dd = panel.Locator("dd").First;
        await dd.WaitForAsync(new() { Timeout = 10_000 });
        return (await dd.TextContentAsync())?.Trim();
    }

    public async Task AcknowledgeAssetAsync()
    {
        await Dialog.Locator("[data-testid='acknowledge-btn']").ClickAsync();
        await WaitForCompletedAsync();
    }


    public async Task CompleteGeneralTaskAsync()
    {
        await Dialog.Locator("[data-testid='complete-task-btn']").ClickAsync();
        await WaitForCompletedAsync();
    }


    public async Task<bool> HasAssetReturnPanelAsync() =>
        await Dialog.Locator("[data-testid='asset-return-panel']").IsVisibleAsync();

    public async Task<string?> GetReturnAssetNumberAsync()
    {
        var panel = Dialog.Locator("[data-testid='asset-return-panel']");
        var dd = panel.Locator("dd").First;
        await dd.WaitForAsync(new() { Timeout = 10_000 });
        return (await dd.TextContentAsync())?.Trim();
    }

    public async Task ConfirmReturnAsync()
    {
        await Dialog.Locator("[data-testid='return-btn']").ClickAsync();
        await WaitForCompletedAsync();
    }


    public async Task<bool> HasProbationReviewPanelAsync() =>
        await Dialog.Locator("[data-testid='probation-review-panel']").IsVisibleAsync();

    public async Task<string?> GetProbationReviewTypeAsync()
    {
        var el = Dialog.Locator("[data-testid='review-type']");
        return await el.IsVisibleAsync() ? (await el.TextContentAsync())?.Trim() : null;
    }

    public async Task EnterReviewNotesAsync(string notes)
    {
        await Dialog.GetByPlaceholder("Enter your review notes…").FillAsync(notes);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SelectReviewOutcomeAsync(string outcome) =>
        await DropDownSelector.SelectAsync(page, Dialog.Locator("[data-testid='probation-review-panel']"), outcome);

    public async Task EnterFailureReasonAsync(string reason)
    {
        await Dialog.GetByPlaceholder("Enter the reason for failing probation…").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task CompleteReviewAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Complete Review" }).ClickAsync();
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Done" }).ClickAsync();
        await WaitForCompletedAsync();
    }


    public async Task<bool> HasSharedDocumentAcknowledgementPanelAsync() =>
        await Dialog.Locator("[data-testid='shared-document-acknowledgement-panel']").IsVisibleAsync();

    public async Task ClickViewAndAcknowledgeDocumentAsync()
    {
        await Dialog.Locator("[data-testid='view-document-btn']").ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/shared-documents/published/"), new() { Timeout = 15_000 });
    }
}
