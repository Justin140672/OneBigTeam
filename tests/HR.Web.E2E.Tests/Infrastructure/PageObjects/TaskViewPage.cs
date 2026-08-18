using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Interacts with TaskViewDialog (src/HR.Web/Components/Pages/Tasks/TaskViewDialog.razor),
/// which replaced the old standalone "/companies/{id}/tasks/{taskId}" page. There is no longer
/// a URL that opens a specific task directly — the dialog must be opened by clicking a task
/// row somewhere in the UI. GoToAsync uses the employee's own "My Profile" Tasks tab
/// (TaskList.razor), which renders a stable data-testid per row keyed by task id; other entry
/// points (dashboard widget, notification bell) are handled by HrDashboardPage/
/// RecruitmentDashboardPage/ManagerDashboardPage/NotificationPanel, which open the same dialog
/// and hand control back here for reading its content.
/// </summary>
public sealed class TaskViewPage(IPage page, string baseUrl)
{
    // Scoped to [role='dialog'] because Syncfusion's SfDialog CssClass propagates onto multiple
    // elements (the outer container, the dialog itself, and the close button), which makes a
    // bare ".task-view-dialog" locator ambiguous under Playwright's strict mode.
    private ILocator Dialog => page.Locator("[role='dialog'].task-view-dialog");

    /// <summary>Returns true if the task dialog is currently open/visible.</summary>
    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    /// <summary>
    /// Navigates to the given employee's own Tasks tab and opens the specified task's dialog.
    /// The employee must be the currently logged-in user (this is the self-service route).
    /// </summary>
    public async Task GoToAsync(Guid companyId, Guid employeeId, Guid taskId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}/profile?tab=tasks");

        var row = page.Locator($"[data-testid='task-view-btn-{taskId}']");
        await row.WaitForAsync(new() { Timeout = 20_000 });
        await row.ClickAsync();

        await WaitForLoadedAsync();
    }

    /// <summary>
    /// Navigates to the given employee's own Tasks tab and opens whichever task row's title
    /// exactly matches <paramref name="taskTitle"/>, rather than a known task id. Used when the
    /// task was just created as a side effect of another action (e.g. CreateAssetAssignmentHandler
    /// auto-creating an "Acknowledge receipt of asset" task) so its id isn't known ahead of time —
    /// TaskList.razor's row title span always renders the task's exact Title text (see its
    /// data-testid="task-view-btn-{item.Id}" sibling attribute), so matching on title is reliable
    /// as long as the title is unique among the employee's current tasks, same assumption
    /// GoToAsync's per-id lookup makes about there being exactly one matching row.
    /// The employee must be the currently logged-in user (this is the self-service route).
    /// </summary>
    public async Task GoToByTitleAsync(Guid companyId, Guid employeeId, string taskTitle)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}/profile?tab=tasks");

        var row = page.GetByRole(AriaRole.Button, new() { Name = taskTitle, Exact = true });
        await row.WaitForAsync(new() { Timeout = 20_000 });
        await row.ClickAsync();

        await WaitForLoadedAsync();
    }

    /// <summary>
    /// Waits for the dialog to be visible and its content to finish loading — the header shows
    /// a "Task" placeholder while the task detail is still being fetched, so this waits for the
    /// title to become anything else. Call this after opening the dialog via any entry point
    /// other than <see cref="GoToAsync"/> (which already calls it).
    /// </summary>
    public async Task WaitForLoadedAsync()
    {
        await Dialog.WaitForAsync(new() { Timeout = 15_000 });
        await page.WaitForFunctionAsync(
            "document.querySelector('.task-view-dialog [data-testid=\"task-title\"]')?.textContent?.trim() !== 'Task'",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        // The title flipping away from the "Task" placeholder above only proves the task's own
        // header data has loaded — the type-specific panel below it (acknowledgement, return,
        // document upload, leave review, probation review, etc.) is a separate conditional render
        // that can still mount a tick later, same Syncfusion/Blazor "container before content"
        // race already fixed on EmployeeAdminPage/MyProfilePage's asset grids and
        // EmployeeEditPage's new-employee save. Callers that immediately call a Has*PanelAsync
        // check right after GoToAsync/GoToByTitleAsync can otherwise see "not visible" for a panel
        // that's genuinely there a moment later. Wait for whichever panel actually renders for
        // this task's type, or the plain "Mark as Complete" button for a general task with no
        // dedicated panel at all.
        await page.WaitForSelectorAsync(
            ".task-view-dialog [data-testid$='-panel'], .task-view-dialog [data-testid='complete-task-btn'], .task-view-dialog .card-header",
            new() { Timeout = 15_000 });
    }

    /// <summary>Returns the task title shown in the dialog header.</summary>
    public async Task<string> GetTitleAsync() =>
        (await Dialog.Locator("[data-testid='task-title']").TextContentAsync())?.Trim() ?? "";

    /// <summary>Returns the task description paragraph text, or null if absent.</summary>
    public async Task<string?> GetDescriptionAsync()
    {
        var p = Dialog.Locator(".card-body p.mb-3").First;
        return await p.IsVisibleAsync() ? (await p.TextContentAsync())?.Trim() : null;
    }

    /// <summary>Returns the value of a detail row identified by its label text.</summary>
    public async Task<string?> GetDetailAsync(string label)
    {
        var dt = Dialog.Locator("dl.row dt").Filter(new() { HasText = label }).First;
        // A bare instant IsVisibleAsync() right after opening the dialog can race the detail
        // list's own render (the same panel-content race WaitForLoadedAsync now waits for at the
        // panel level, but individual dt/dd rows can still land a tick later) and return null
        // before the row has actually appeared. Give it a bounded wait instead of failing fast.
        try
        {
            await dt.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }
        // The corresponding dd follows the dt in DOM order.
        return (await dt.Locator("~ dd").First.TextContentAsync())?.Trim();
    }

    /// <summary>
    /// Returns "Completed" once the dialog's detail list shows a "Completed" row — the only
    /// status signal left in the dialog now that the status badge has been removed — otherwise
    /// "Not Started". None of the current E2E scenarios distinguish Open from InProgress, so
    /// both collapse to "Not Started" here.
    /// </summary>
    public async Task<string> GetStatusAsync()
    {
        var completedRow = Dialog.Locator("dl.row dt").Filter(new() { HasText = "Completed" });
        return await completedRow.IsVisibleAsync() ? "Completed" : "Not Started";
    }

    /// <summary>Waits for the dialog's detail list to show a "Completed" row.</summary>
    public async Task WaitForCompletedAsync() =>
        await page.WaitForFunctionAsync(
            @"() => Array.from(document.querySelectorAll('.task-view-dialog dl.row dt'))
                .some(dt => dt.textContent?.trim() === 'Completed')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

    /// <summary>Closes the dialog via its Close button.</summary>
    public async Task CloseAsync()
    {
        // TaskViewDialog.razor renders both Syncfusion's built-in "X" close icon (ShowCloseIcon,
        // which also carries aria-label="Close") and an explicit <SfButton>Close</SfButton> in
        // the footer — both share the accessible name "Close", so GetByRole(Button, "Close")
        // is ambiguous under Playwright's strict mode. GetByText targets only the labeled
        // button, since the icon button has no text content.
        await Dialog.GetByText("Close", new() { Exact = true }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    // ── Leave review panel ───────────────────────────────────────────────────

    /// <summary>Returns true if the "Review Leave Request" card is present and active.</summary>
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

    // ── Document upload panel ────────────────────────────────────────────────

    /// <summary>Returns true if the document upload panel is present and visible.</summary>
    public async Task<bool> HasDocumentUploadPanelAsync() =>
        await Dialog.Locator("[data-testid='document-upload-panel']").IsVisibleAsync();

    /// <summary>Fills the title input in the document upload panel.</summary>
    public async Task SetDocumentTitleAsync(string title)
    {
        var panel = Dialog.Locator("[data-testid='document-upload-panel']");
        var input = panel.GetByPlaceholder("Document title");
        await input.ClearAsync();
        await input.FillAsync(title);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Sets the file to be uploaded via the document upload panel's file input.</summary>
    public async Task AttachUploadFileAsync(string filePath)
    {
        var fileInput = Dialog.Locator("[data-testid='document-upload-panel'] input[type='file']");
        await fileInput.SetInputFilesAsync(filePath);
    }

    /// <summary>Clicks "Upload Document" and waits for the task to become Completed.</summary>
    public async Task SubmitDocumentUploadAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();
        await WaitForCompletedAsync();
    }

    // ── Asset acknowledgement panel ──────────────────────────────────────────

    /// <summary>Returns true if the asset acknowledgement panel is present and visible.</summary>
    public async Task<bool> HasAssetAcknowledgementPanelAsync() =>
        await Dialog.Locator("[data-testid='asset-acknowledgement-panel']").IsVisibleAsync();

    /// <summary>Returns the asset number shown in the acknowledgement panel, waiting for async load.</summary>
    public async Task<string?> GetAcknowledgementAssetNumberAsync()
    {
        var panel = Dialog.Locator("[data-testid='asset-acknowledgement-panel']");
        var dd = panel.Locator("dd").First;
        await dd.WaitForAsync(new() { Timeout = 10_000 });
        return (await dd.TextContentAsync())?.Trim();
    }

    /// <summary>Clicks "I Acknowledge Receipt" and waits for the task to become Completed.</summary>
    public async Task AcknowledgeAssetAsync()
    {
        await Dialog.Locator("[data-testid='acknowledge-btn']").ClickAsync();
        await WaitForCompletedAsync();
    }

    // ── General task panel ───────────────────────────────────────────────────

    /// <summary>Clicks "Mark as Complete" on a general task and waits for it to become Completed.</summary>
    public async Task CompleteGeneralTaskAsync()
    {
        await Dialog.Locator("[data-testid='complete-task-btn']").ClickAsync();
        await WaitForCompletedAsync();
    }

    // ── Asset return panel ───────────────────────────────────────────────────

    /// <summary>Returns true if the asset return panel is present and visible.</summary>
    public async Task<bool> HasAssetReturnPanelAsync() =>
        await Dialog.Locator("[data-testid='asset-return-panel']").IsVisibleAsync();

    /// <summary>Returns the asset number shown in the return panel, waiting for async load.</summary>
    public async Task<string?> GetReturnAssetNumberAsync()
    {
        var panel = Dialog.Locator("[data-testid='asset-return-panel']");
        var dd = panel.Locator("dd").First;
        await dd.WaitForAsync(new() { Timeout = 10_000 });
        return (await dd.TextContentAsync())?.Trim();
    }

    /// <summary>Clicks "Confirm Return" and waits for the task to become Completed.</summary>
    public async Task ConfirmReturnAsync()
    {
        await Dialog.Locator("[data-testid='return-btn']").ClickAsync();
        await WaitForCompletedAsync();
    }

    // ── Probation review panel ───────────────────────────────────────────────

    /// <summary>Returns true if the "Complete Probation Review" card is present.</summary>
    public async Task<bool> HasProbationReviewPanelAsync() =>
        await Dialog.Locator("[data-testid='probation-review-panel']").IsVisibleAsync();

    /// <summary>Returns the review type text shown in the probation review panel.</summary>
    public async Task<string?> GetProbationReviewTypeAsync()
    {
        var el = Dialog.Locator("[data-testid='review-type']");
        return await el.IsVisibleAsync() ? (await el.TextContentAsync())?.Trim() : null;
    }

    /// <summary>Fills the review notes textarea.</summary>
    public async Task EnterReviewNotesAsync(string notes)
    {
        await Dialog.GetByPlaceholder("Enter your review notes…").FillAsync(notes);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Clicks "Complete Review", dismisses the confirmation, and waits for the task to become Completed.</summary>
    public async Task CompleteReviewAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Complete Review" }).ClickAsync();
        // Panel shows "Review completed." with a Done button before reloading the task.
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Done" }).ClickAsync();
        await WaitForCompletedAsync();
    }

    // ── Shared document acknowledgement panel ────────────────────────────────
    // Unlike the other panels above, this one never completes the task inline — its button
    // navigates away to SharedCompanyDocumentAcknowledgement.razor (see
    // SharedCompanyDocumentAcknowledgementPage), where the employee actually checks the
    // confirmation checkbox and confirms. Once acknowledged, the panel itself just shows an
    // "Acknowledged on …" alert instead of the button.

    /// <summary>Returns true if the shared document acknowledgement panel is present and visible.</summary>
    public async Task<bool> HasSharedDocumentAcknowledgementPanelAsync() =>
        await Dialog.Locator("[data-testid='shared-document-acknowledgement-panel']").IsVisibleAsync();

    /// <summary>
    /// Clicks "View & Acknowledge Document" and waits for the browser to navigate away to the
    /// document's acknowledgement page — this panel does not complete the task itself, so
    /// callers must drive the rest of the flow via SharedCompanyDocumentAcknowledgementPage.
    /// </summary>
    public async Task ClickViewAndAcknowledgeDocumentAsync()
    {
        await Dialog.Locator("[data-testid='view-document-btn']").ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/shared-documents/published/"), new() { Timeout = 15_000 });
    }
}
