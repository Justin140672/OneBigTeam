using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using HR.Web.Services;

namespace HR.Web.Components.Pages;

/// <summary>
/// Shared chrome for routed edit/create/view pages: loading state, view-mode detection from
/// the URL, a Save pipeline (validate → save → success/error), and a Close action that warns
/// about unsaved changes before navigating back to <see cref="ListUrl"/>. Pages with their own
/// model should use <see cref="EditPageBase{TModel}"/>; pages that only orchestrate child
/// sections (e.g. a tabbed page whose tabs each own their own model) use this non-generic base
/// directly.
/// </summary>
public abstract class EditPageBase : ComponentBase
{
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    protected bool IsLoading { get; set; } = true;
    protected bool IsViewMode { get; private set; }
    protected bool Saving { get; private set; }
    protected string? GlobalError { get; set; }
    protected string? SuccessMsg { get; set; }
    protected bool ShowUnsavedChangesDialog { get; set; }

    // The list page to return to on Close, and to navigate to after a successful save (unless
    // OnSavedAsync is overridden). Leave null for pages that should stay put after saving.
    protected virtual string? ListUrl => null;

    // Overridden by EditPageBase<TModel> (diffs the model against a load-time snapshot) and by
    // orchestrator pages that also want to factor in a child section's unsaved state.
    protected virtual bool HasUnsavedChanges => false;

    protected override async Task OnParametersSetAsync()
    {
        IsViewMode = Navigation.Uri.Contains("/view", StringComparison.OrdinalIgnoreCase);
        IsLoading = true;
        GlobalError = null;
        SuccessMsg = null;

        await LoadAsync();
        CaptureBaseline();

        IsLoading = false;
    }

    protected virtual Task LoadAsync() => Task.CompletedTask;

    // Overridden by EditPageBase<TModel> to snapshot the model for HasUnsavedChanges comparison.
    protected virtual void CaptureBaseline() { }

    // Overridden by EditPageBase<TModel> to run EditContext.Validate(). Pages with no model of
    // their own (pure orchestrators) leave this as-is; their child sections validate themselves.
    protected virtual bool Validate() => true;

    protected async Task SaveAsync()
    {
        GlobalError = null;
        SuccessMsg = null;

        if (!Validate())
        {
            GlobalError = "Please correct the highlighted fields below.";
            return;
        }

        Saving = true;
        StateHasChanged();

        var error = await SaveCoreAsync();
        Saving = false;

        if (error is null)
        {
            CaptureBaseline();
            await OnSavedAsync();
        }
        else
        {
            GlobalError = error;
        }
    }

    // Perform the save and return null on success, or an error message to show as GlobalError.
    protected abstract Task<string?> SaveCoreAsync();

    // Default: navigate to ListUrl if set, otherwise stay on the page and show an inline
    // success banner (multi-section pages like CompanyEdit that don't set ListUrl).
    protected virtual Task OnSavedAsync()
    {
        if (ListUrl is not null)
            Navigation.NavigateTo(ListUrl, forceLoad: true);
        else
            SuccessMsg = "Saved successfully.";

        return Task.CompletedTask;
    }

    // Wired to the Close button. Prompts to save first if there are unsaved changes.
    protected void RequestClose()
    {
        if (HasUnsavedChanges)
            ShowUnsavedChangesDialog = true;
        else
            NavigateToList();
    }

    protected void NavigateToList()
    {
        if (ListUrl is not null)
            Navigation.NavigateTo(ListUrl);
    }

    // Save, then always navigate to ListUrl on success — even for pages whose normal
    // OnSavedAsync stays put (e.g. an orchestrator page showing an inline success banner),
    // since the user explicitly chose "Save" from the "unsaved changes" close prompt.
    protected async Task ConfirmSaveAndCloseAsync()
    {
        ShowUnsavedChangesDialog = false;
        await SaveAsync();

        if (GlobalError is null)
            NavigateToList();
    }

    protected void DiscardChangesAndClose()
    {
        ShowUnsavedChangesDialog = false;
        NavigateToList();
    }

    protected void CancelCloseDialog() => ShowUnsavedChangesDialog = false;
}

/// <summary>
/// <see cref="EditPageBase"/> plus a single owned <typeparamref name="TModel"/> validated via
/// <see cref="EditContext"/>/DataAnnotations. The <see cref="Model"/> instance is created once
/// and repopulated in place by <see cref="EditPageBase.LoadAsync"/> whenever route parameters
/// change, so the EditContext never needs to be recreated.
/// </summary>
public abstract class EditPageBase<TModel> : EditPageBase where TModel : class, new()
{
    protected TModel Model { get; } = new();
    protected EditContext EditContext { get; private set; } = default!;

    private string? _baselineSnapshot;

    protected override void OnInitialized()
    {
        EditContext = new EditContext(Model);
        base.OnInitialized();
    }

    protected override bool Validate() => EditContext.Validate();

    protected override void CaptureBaseline() =>
        _baselineSnapshot = System.Text.Json.JsonSerializer.Serialize(Model);

    protected override bool HasUnsavedChanges =>
        _baselineSnapshot is not null && _baselineSnapshot != System.Text.Json.JsonSerializer.Serialize(Model);
}

/// <summary>
/// <see cref="EditPageBase{TModel}"/> for the common "simple" shape: one entity, loaded/created/
/// updated through an <see cref="IEditService{TModel, TKey}"/>. Pushes the load/save boilerplate
/// that every such page (Department, EmploymentType, LeaveType, ...) otherwise duplicates.
/// Pages with extra needs (e.g. a dropdown's candidate list) override <see cref="OnLoadedAsync"/>.
/// </summary>
public abstract class EditPageBase<TModel, TKey> : EditPageBase<TModel>
    where TModel : class, new()
    where TKey : struct
{
    protected abstract IEditService<TModel, TKey> Service { get; }
    protected abstract Guid GetCompanyId();
    protected abstract TKey? GetId();

    protected virtual bool IsNew => GetId() is null;

    protected override async Task LoadAsync()
    {
        if (!IsNew)
        {
            var loaded = await Service.GetByIdAsync(GetCompanyId(), GetId()!.Value);
            if (loaded is not null) CopyProperties(loaded, Model);
        }

        await OnLoadedAsync();
    }

    // Hook for whatever a page needs beyond its own entity (e.g. a parent-picker dropdown list).
    protected virtual Task OnLoadedAsync() => Task.CompletedTask;

    protected override async Task<string?> SaveCoreAsync()
    {
        var (result, error) = IsNew
            ? await Service.CreateAsync(GetCompanyId(), Model)
            : await Service.UpdateAsync(GetCompanyId(), GetId()!.Value, Model);

        return result is not null ? null : error ?? "Failed to save.";
    }

    // Model must stay the same instance for the whole page lifetime (EditContext is bound to it
    // once) so a freshly-loaded entity is copied in place rather than replacing Model outright.
    private static void CopyProperties(TModel source, TModel target)
    {
        foreach (var prop in typeof(TModel).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.CanRead && prop.CanWrite)
                prop.SetValue(target, prop.GetValue(source));
        }
    }
}
