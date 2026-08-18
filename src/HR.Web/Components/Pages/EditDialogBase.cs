using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Components.Pages;

/// <summary>
/// Shared chrome for SfDialog-based create/edit dialogs: open/close state driven by
/// <see cref="IsOpen"/>, a single owned <typeparamref name="TModel"/> validated via
/// <see cref="EditContext"/>/DataAnnotations, a Submit pipeline (validate → save → reset
/// → notify parent, or show an error), and a Cancel that warns about unsaved changes before
/// discarding them.
/// </summary>
public abstract class EditDialogBase<TModel> : ComponentBase where TModel : class, new()
{
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnCancelled { get; set; }

    protected TModel Model { get; } = new();
    protected EditContext EditContext { get; private set; } = default!;

    // Normally driven by IsOpen; settable directly by dialogs that open via their own public
    // method (e.g. one that needs to receive data at open time) instead of a parameter toggle.
    protected bool Visible { get; set; }
    protected bool Saving { get; private set; }
    protected string? GlobalError { get; set; }
    protected bool ShowUnsavedChangesDialog { get; set; }

    private string? _baselineSnapshot;

    protected virtual bool HasUnsavedChanges =>
        _baselineSnapshot is not null && _baselineSnapshot != System.Text.Json.JsonSerializer.Serialize(Model);

    protected override void OnInitialized()
    {
        EditContext = new EditContext(Model);
        base.OnInitialized();
    }

    protected override async Task OnParametersSetAsync()
    {
        var wasOpen = Visible;
        Visible = IsOpen;

        if (IsOpen && !wasOpen)
        {
            await OnOpenedAsync();
            CaptureBaseline();
        }
    }

    // Called each time the dialog transitions from closed to open — use this to lazily load
    // dropdown data etc. (replaces each dialog's previous ad-hoc "if open and not loaded" check).
    protected virtual Task OnOpenedAsync() => Task.CompletedTask;

    private void CaptureBaseline() =>
        _baselineSnapshot = System.Text.Json.JsonSerializer.Serialize(Model);

    protected async Task SubmitAsync()
    {
        GlobalError = null;

        if (!EditContext.Validate())
        {
            GlobalError = "Please correct the highlighted fields below.";
            return;
        }

        var extraError = ValidateExtra();
        if (extraError is not null)
        {
            GlobalError = extraError;
            return;
        }

        Saving = true;
        StateHasChanged();

        var error = await SaveCoreAsync();
        Saving = false;

        if (error is not null)
        {
            GlobalError = error;
            return;
        }

        ResetForm();
        await OnSaved.InvokeAsync();
    }

    // Hook for the handful of checks DataAnnotations can't express (e.g. "a file must be
    // selected"). Runs after DataAnnotations validation passes. Return null if OK.
    protected virtual string? ValidateExtra() => null;

    // Perform the save and return null on success, or an error message to show as GlobalError.
    protected abstract Task<string?> SaveCoreAsync();

    // Clear all fields back to their defaults after a successful submit or a cancel.
    protected abstract void ResetForm();

    // Wired to the Cancel button and the dialog's close icon/Escape key. Prompts to save first
    // if there are unsaved changes, rather than silently discarding them.
    protected async Task CancelAsync()
    {
        if (HasUnsavedChanges)
        {
            ShowUnsavedChangesDialog = true;
            return;
        }

        await DiscardAndCancelAsync();
    }

    private async Task DiscardAndCancelAsync()
    {
        // Close immediately rather than waiting for the parent to round-trip IsOpen back down
        // through OnParametersSetAsync — that extra async hop (parent event handler -> re-render
        // -> parameter push) can lag a tick behind under load, leaving the dialog visibly open
        // even though the "discard" decision has already been made.
        Visible = false;
        ResetForm();
        await OnCancelled.InvokeAsync();
    }

    protected async Task ConfirmSaveInsteadOfCancelAsync()
    {
        ShowUnsavedChangesDialog = false;
        await SubmitAsync();
    }

    protected async Task ConfirmDiscardAsync()
    {
        ShowUnsavedChangesDialog = false;
        await DiscardAndCancelAsync();
    }

    protected void CancelUnsavedChangesDialog() => ShowUnsavedChangesDialog = false;

    protected Task HandleDialogClosed() => CancelAsync();
}
