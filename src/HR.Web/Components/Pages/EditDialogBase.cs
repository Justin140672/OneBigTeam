using HR.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Components.Pages;

public abstract class EditDialogBase<TModel> : ComponentBase where TModel : class, new()
{
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnCancelled { get; set; }

    [Inject] protected AppSession ReadOnlyAppSession { get; set; } = default!;

    protected bool IsSubscriptionReadOnly => ReadOnlyAppSession.IsReadOnly;

    protected TModel Model { get; } = new();
    protected EditContext EditContext { get; private set; } = default!;

    protected bool Visible { get; set; }
    protected bool Saving { get; private set; }
    protected string? GlobalError { get; set; }
    protected bool ShowUnsavedChangesDialog { get; set; }

    // Ticket 2 (optimistic concurrency): true when the last save was rejected with HTTP 409 /
    // code "concurrency". Drives the shared <SaveConflictBanner>, which takes precedence over the
    // generic GlobalError alert. A SaveCoreAsync implementation sets this from the service result.
    protected bool SaveConflict { get; set; }

    private string? _baselineSnapshot;

    private bool _suppressNextClosedEvent;

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

        if (!IsOpen && wasOpen)
        {
            _suppressNextClosedEvent = true;
        }

        if (IsOpen && !wasOpen)
        {
            await OnOpenedAsync();
            CaptureBaseline();
        }
    }

    protected virtual Task OnOpenedAsync() => Task.CompletedTask;

    private void CaptureBaseline() =>
        _baselineSnapshot = System.Text.Json.JsonSerializer.Serialize(Model);

    protected async Task SubmitAsync()
    {
        GlobalError = null;
        SaveConflict = false;

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

    protected virtual string? ValidateExtra() => null;

    protected abstract Task<string?> SaveCoreAsync();

    protected abstract void ResetForm();

    protected async Task CancelAsync()
    {
        if (ShowUnsavedChangesDialog)
            return;

        if (HasUnsavedChanges)
        {
            ShowUnsavedChangesDialog = true;
            return;
        }

        await DiscardAndCancelAsync();
    }

    private async Task DiscardAndCancelAsync()
    {
        _suppressNextClosedEvent = true;
        Visible = false;
        ResetForm();

        // Only Submit should validate — Cancel/Discard must not leave stale per-field
        // modified/invalid styling visible (see EditPageBase.DiscardChangesAndClose's matching
        // fix for the routed-page equivalent of this dialog).
        EditContext.MarkAsUnmodified();

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

    protected Task HandleDialogClosed()
    {
        if (_suppressNextClosedEvent)
        {
            _suppressNextClosedEvent = false;
            return Task.CompletedTask;
        }

        return CancelAsync();
    }
}
