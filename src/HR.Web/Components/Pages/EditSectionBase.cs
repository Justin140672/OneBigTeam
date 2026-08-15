using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Components.Pages;

/// <summary>
/// Shared chrome for a tab/section embedded in a multi-section edit page (e.g. a tab on
/// EmployeeEdit or CompanyEdit) that saves via a public method called by the parent page's
/// single Save button, rather than a submit button of its own. Owns a single
/// <typeparamref name="TModel"/> validated via <see cref="EditContext"/>/DataAnnotations.
/// </summary>
public abstract class EditSectionBase<TModel> : ComponentBase where TModel : class, new()
{
    protected TModel Model { get; } = new();
    protected EditContext EditContext { get; private set; } = default!;

    protected bool IsLoading { get; set; } = true;
    protected string? GlobalError { get; set; }
    protected string? SuccessMsg { get; set; }

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
        base.OnInitialized();
    }

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

    /// <summary>Called by the parent page's single Save button. Returns null on success, or an error message.</summary>
    public async Task<string?> SaveAsync()
    {
        GlobalError = null;
        SuccessMsg = null;
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
