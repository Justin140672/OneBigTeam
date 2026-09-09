using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using HR.Web.Services;

namespace HR.Web.Components.Pages;

/// <summary>
/// Shared chrome for a tab/section embedded in a multi-section edit page (e.g. a tab on
/// EmployeeEdit or CompanyEdit) that saves via a public method called by the parent page's
/// single Save button, rather than a submit button of its own. Owns a single
/// <typeparamref name="TModel"/> validated via <see cref="EditContext"/>/DataAnnotations.
/// </summary>
public abstract class EditSectionBase<TModel> : ComponentBase, IDisposable where TModel : class, new()
{
    protected TModel Model { get; } = new();
    protected EditContext EditContext { get; private set; } = default!;

    protected bool IsLoading { get; set; } = true;
    protected string? GlobalError { get; set; }
    protected string? SuccessMsg { get; set; }

    // ── Optimistic concurrency (Ticket 2) ────────────────────────────────────────
    // The version this section's Model was loaded at. Set it in LoadAsync from the GET response;
    // it is sent as ExpectedVersion on save (see ExpectedVersionForSave) and refreshed from every
    // successful Update* response by ApplySaveResult.
    protected int? LoadedVersion { get; set; }

    // Set by an orchestrating parent that runs a chained save (e.g. EmployeeEdit saves the profile
    // first, then this tab) and needs this save to send the token produced by the first call
    // rather than the now-stale one loaded with the page.
    public int? ExpectedVersionOverride { get; set; }

    protected int? ExpectedVersionForSave => ExpectedVersionOverride ?? LoadedVersion;

    // True when the last save was rejected by the API with HTTP 409 / code "concurrency" — i.e.
    // the record changed elsewhere since it was loaded. Drives the shared <SaveConflictBanner>.
    public bool SaveConflict { get; private set; }

    // One-liner for a SaveCoreAsync implementation: records the conflict state, refreshes
    // LoadedVersion on success, and returns null (success) or the error message to surface.
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

    // Concurrency-conflict recovery. Re-fetches server state via LoadAsync (which repopulates
    // Model + LoadedVersion) and clears the conflict banner. By default the user's in-progress
    // edits are preserved — only an explicit user-driven reload passes rebaseline: true to adopt
    // the server values and reset the unsaved-changes tracking.
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
        // Bug fix (b): once the form is invalid again after a conflict (the user edited a field to
        // an invalid value before re-saving), drop the stale conflict banner so it doesn't linger
        // alongside the validation errors.
        if (SaveConflict && EditContext.GetValidationMessages().Any())
        {
            SaveConflict = false;
            StateHasChanged();
        }
    }

    private string? _baselineSnapshot;
    private object? _loadedKey;
    private bool _hasLoaded;

    // Public so an orchestrating parent page (e.g. EmployeeEdit, CompanyEdit) can fold this
    // section's unsaved state into its own Close/unsaved-changes check.
    public bool HasUnsavedChanges =>
        _baselineSnapshot is not null && _baselineSnapshot != System.Text.Json.JsonSerializer.Serialize(Model);

    // Identifies which entity this section is currently editing (e.g. a CompanyId parameter).
    // Overridden by sections whose parent can hand them a different entity across the section's
    // lifetime; left null (the default) for sections that only ever load once.
    protected virtual object? LoadKey => null;

    protected override void OnInitialized()
    {
        EditContext = new EditContext(Model);
        EditContext.OnValidationStateChanged += OnValidationStateChanged;
        base.OnInitialized();
    }

    public void Dispose() => EditContext.OnValidationStateChanged -= OnValidationStateChanged;

    // Blazor invokes OnParametersSetAsync every time the parent re-renders, not only when a
    // parameter value actually changes — so without this guard, an unrelated parent StateHasChanged
    // (e.g. toggling a Saving flag) would re-run LoadAsync and clobber whatever the user just typed
    // with a fresh fetch from the server, right before the parent's own save reads this Model.
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

    // Public so an orchestrating parent page can discard this section's changes without saving —
    // e.g. EmployeeEdit's Close/"Discard Changes" flow, which folds this section's HasUnsavedChanges
    // into its own but has no other way to clear it back to false once the user has confirmed
    // discarding (the section's own Model isn't reset; the page reload triggered by the discard
    // makes that unnecessary, this exists purely so HasUnsavedChanges reads false for anything
    // — e.g. NavigationLock — that checks it in the moment between the discard being confirmed and
    // that reload actually happening).
    public void ResetBaseline() => CaptureBaseline();

    /// <summary>Called by the parent page's single Save button. Returns null on success, or an error message.</summary>
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

    // Perform the save and return null on success, or an error message.
    protected abstract Task<string?> SaveCoreAsync();
}
