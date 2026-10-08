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
        if (!_hasLoaded || _disposed) return;

        var preservedEdits = rebaseline
            ? null
            : System.Text.Json.JsonSerializer.Serialize(Model);

        var key = LoadKey;
        var generation = BeginGeneration(out var token);
        Action apply;
        try
        {
            apply = await LoadAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }

        if (!IsCurrent(generation, key)) return;
        apply();
        _loadedKey = key;

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

    public void Dispose()
    {
        _disposed = true;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        EditContext.OnValidationStateChanged -= OnValidationStateChanged;
    }

    private object? _loadingKey;
    private bool _loadInFlight;
    private int _generation;
    private bool _disposed;
    private CancellationTokenSource? _loadCts;

    protected virtual void RequestRender() => StateHasChanged();

    protected object? LoadedKey => _loadedKey;

    protected bool CanSave => _hasLoaded && !IsLoading && Equals(_loadedKey, LoadKey);

    private int BeginGeneration(out CancellationToken token)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        token = _loadCts.Token;
        return ++_generation;
    }

    private bool IsCurrent(int generation, object? key) =>
        !_disposed && generation == _generation && Equals(LoadKey, key);

    protected override async Task OnParametersSetAsync()
    {
        var key = LoadKey;

        if (_hasLoaded && Equals(_loadedKey, key))
            return;

        if (_loadInFlight && Equals(_loadingKey, key))
            return;

        var generation = BeginGeneration(out var token);
        _loadInFlight = true;
        _loadingKey = key;
        _hasLoaded = false;
        _loadedKey = null;
        IsLoading = true;
        GlobalError = null;
        SuccessMsg = null;

        try
        {
            var apply = await LoadAsync(token);

            if (!IsCurrent(generation, key)) return;

            apply();
            CaptureBaseline();

            _loadedKey = key;
            _hasLoaded = true;
            IsLoading = false;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            if (generation == _generation)
                _loadInFlight = false;
        }
    }

    // Must capture every route/resource id into locals before its first await and return an
    // action that applies the fetched data to Model/fields; the base only invokes that action if
    // this load is still the current generation for the current LoadKey.
    protected abstract Task<Action> LoadAsync(CancellationToken cancellationToken);

    private void CaptureBaseline() =>
        _baselineSnapshot = System.Text.Json.JsonSerializer.Serialize(Model);

    public void ResetBaseline() => CaptureBaseline();

    public async Task<string?> SaveAsync()
    {
        GlobalError = null;
        SuccessMsg = null;
        SaveConflict = false;

        if (!CanSave)
        {
            GlobalError = "The form is still loading or is out of date. Please wait and try again.";
            return GlobalError;
        }

        RequestRender();

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
