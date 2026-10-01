using HR.Web.Components.Controls;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using HR.Web.Services;

namespace HR.Web.Components.Pages;

public abstract class EditSectionBase<TModel> : ComponentBase, IDisposable where TModel : class, new()
{
    [Inject] protected AppSession ReadOnlyAppSession { get; set; } = default!;

    protected bool IsSubscriptionReadOnly => ReadOnlyAppSession.IsReadOnly;

    protected TModel Model { get; } = new();
    protected EditContext EditContext { get; private set; } = default!;

    protected Dictionary<string, object> Aria(string id, string? property, bool required = false, bool emitId = true, string? label = null) =>
        FieldAria.Build(EditContext, Model, id, property, required, emitId, label);

    protected bool IsLoading { get; set; } = true;
    protected string? GlobalError { get; set; }
    protected string? SuccessMsg { get; set; }

    // ── Optimistic concurrency (Ticket 2) ────────────────────────────────────────
    // The version this section's Model was loaded at. Set it in LoadAsync from the GET response;
    // it is sent as ExpectedVersion on save (see ExpectedVersionForSave) and refreshed from every
    // successful Update* response by ApplySaveResult.
    protected int? LoadedVersion { get; set; }

    public int? ExpectedVersionOverride { get; set; }

    protected int? ExpectedVersionForSave => ExpectedVersionOverride ?? LoadedVersion;

    // True when the last save was rejected by the API with HTTP 409 / code "concurrency" — i.e.
    // the record changed elsewhere since it was loaded. Drives the shared <SaveConflictBanner>.
    public bool SaveConflict { get; private set; }

    protected string? ApplySaveResult(ApiSaveResult result, string? fallbackError = null)
    {
        SaveConflict = result.IsConcurrencyConflict;
        if (result.Success)
        {
            if (result.NewVersion is { } v) LoadedVersion = v;
            return null;
        }
        return result.ErrorMessage ?? fallbackError ?? "Failed to save.";
    }

    public async Task ReloadLatestValuesAsync(bool rebaseline = false)
    {
        var preservedEdits = rebaseline
            ? null
            : System.Text.Json.JsonSerializer.Serialize(Model);

        await LoadAsync();

        if (preservedEdits is not null)
            RestoreModelState(preservedEdits);
        else
            CaptureBaseline();

        SaveConflict = false;
        GlobalError = null;
        StateHasChanged();
    }

    private void RestoreModelState(string json)
    {
        var restored = System.Text.Json.JsonSerializer.Deserialize<TModel>(json);
        if (restored is null) return;
        foreach (var prop in typeof(TModel).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.CanRead && prop.CanWrite)
                prop.SetValue(Model, prop.GetValue(restored));
        }
    }

    private void OnValidationStateChanged(object? sender, ValidationStateChangedEventArgs e)
    {
        if (SaveConflict && EditContext.GetValidationMessages().Any())
        {
            SaveConflict = false;
            StateHasChanged();
        }
    }

    private string? _baselineSnapshot;
    private object? _loadedKey;
    private bool _hasLoaded;

    public bool HasUnsavedChanges =>
        _baselineSnapshot is not null && _baselineSnapshot != System.Text.Json.JsonSerializer.Serialize(Model);

    protected virtual object? LoadKey => null;

    protected override void OnInitialized()
    {
        EditContext = new EditContext(Model);
        EditContext.OnValidationStateChanged += OnValidationStateChanged;
        base.OnInitialized();
    }

    public void Dispose() => EditContext.OnValidationStateChanged -= OnValidationStateChanged;

    protected override async Task OnParametersSetAsync()
    {
        if (_hasLoaded && Equals(_loadedKey, LoadKey))
            return;

        IsLoading = true;
        GlobalError = null;
        SuccessMsg = null;

        await LoadAsync();
        CaptureBaseline();

        _loadedKey = LoadKey;
        _hasLoaded = true;
        IsLoading = false;
    }

    protected abstract Task LoadAsync();

    private void CaptureBaseline() =>
        _baselineSnapshot = System.Text.Json.JsonSerializer.Serialize(Model);

    public void ResetBaseline() => CaptureBaseline();

    public async Task<string?> SaveAsync()
    {
        GlobalError = null;
        SuccessMsg = null;
        SaveConflict = false;
        StateHasChanged();

        if (!EditContext.Validate())
        {
            GlobalError = "Please correct the highlighted fields above.";
            return GlobalError;
        }

        var error = await SaveCoreAsync();

        if (error is null)
        {
            CaptureBaseline();
            SuccessMsg = "Saved.";
        }
        else
        {
            GlobalError = error;
        }

        return error;
    }

    protected abstract Task<string?> SaveCoreAsync();
}
