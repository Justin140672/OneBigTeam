using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.WebUtilities;
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
public abstract class EditPageBase : ComponentBase, IDisposable
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

    // Where Close/Save-and-close should actually go: a "?returnUrl=" query param set by callers
    // that sent the user here from somewhere other than ListUrl (e.g. the Getting Started
    // checklist), falling back to ListUrl when absent. Only trusts an app-relative path (starts
    // with "/", not "//") so a crafted external returnUrl can't redirect the user off-site.
    private string? ReturnUrl
    {
        get
        {
            var uri = Navigation.ToAbsoluteUri(Navigation.Uri);
            if (!QueryHelpers.ParseQuery(uri.Query).TryGetValue("returnUrl", out var value))
                return null;

            var returnUrl = value.ToString();
            return returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") ? returnUrl : null;
        }
    }

    private string? TargetListUrl => ReturnUrl ?? ListUrl;

    // Overridden by EditPageBase<TModel> (diffs the model against a load-time snapshot) and by
    // orchestrator pages that also want to factor in a child section's unsaved state.
    protected virtual bool HasUnsavedChanges => false;

    // Where the user was actually trying to go when a navigation attempt got intercepted by
    // HandleLocationChangingAsync — null when the dialog was triggered by the Close button
    // instead, in which case NavigateToList() falls back to ListUrl.
    private string? _pendingNavigationUri;
    private IDisposable? _locationChangingRegistration;

    // Set immediately before a NavigateToList()-driven NavigateTo call so the interceptor lets
    // that one navigation through unchallenged — otherwise e.g. DiscardChangesAndClose's own
    // follow-up navigation would trip the same guard again (HasUnsavedChanges is still true;
    // discarding doesn't reset the model, it just abandons this component) and the dialog would
    // reopen in an infinite loop instead of actually leaving the page.
    private bool _navigationConfirmed;

    // Lets a subclass issue its own same-page NavigateTo (e.g. updating a "?tab=" query param to
    // reflect the active tab) without tripping the unsaved-changes guard above — that guard exists
    // to catch the user navigating AWAY from a dirty page, not a page updating its own URL to
    // reflect in-page UI state. Must be called immediately before the NavigateTo call it's meant
    // to cover, same as the internal _navigationConfirmed usages in this class.
    protected void SuppressNextNavigationGuard() => _navigationConfirmed = true;

    protected override void OnInitialized()
    {
        // Catches in-app navigation attempts (NavLink clicks, menu items, NavigateTo calls)
        // while the page is dirty — e.g. clicking away to another section without saving.
        // Browser-level navigation (back/forward, refresh, closing the tab) is a separate
        // concern: each edit page renders its own <NavigationLock ConfirmExternalNavigation>
        // next to <UnsavedChangesDialog>, which triggers the browser's native "leave site?"
        // prompt instead — that prompt can't be replaced with our custom dialog, it's a
        // platform (beforeunload) limitation.
        _locationChangingRegistration = Navigation.RegisterLocationChangingHandler(HandleLocationChangingAsync);
    }

    private ValueTask HandleLocationChangingAsync(LocationChangingContext context)
    {
        if (_navigationConfirmed)
        {
            _navigationConfirmed = false;
            return ValueTask.CompletedTask;
        }

        if (!HasUnsavedChanges || ShowUnsavedChangesDialog)
            return ValueTask.CompletedTask;

        context.PreventNavigation();
        _pendingNavigationUri = context.TargetLocation;
        ShowUnsavedChangesDialog = true;
        StateHasChanged();
        return ValueTask.CompletedTask;
    }

    // The URL path (ignoring query string) that the current Model was loaded for. Blazor calls
    // SetParametersAsync -> OnParametersSetAsync on EVERY re-render of this component's parent
    // (MainLayout re-renders on persona switch, AppSession.Changed, LocationChanged, theme load,
    // the employee-completion dialog closing, an AdminQuickNav search, ...), not only when this
    // page's route parameters actually change. Without this guard every one of those parent
    // re-renders flips the page back to its loading spinner and re-runs LoadAsync — which unmounts
    // the form mid-edit (losing text the user has typed but not yet blurred) and, in edit mode,
    // overwrites their changes with a fresh server copy. Only (re)load when the thing being edited
    // actually changes: a real navigation to a different route (new <-> {id} <-> {id}/view).
    private string? _loadedForPath;

    // Query-string changes don't change identity by default (a "?tab=" or "?returnUrl=" tweak must
    // not reload the page). A page that genuinely needs to reload on a query change overrides this.
    protected virtual string LoadIdentity() =>
        Navigation.ToAbsoluteUri(Navigation.Uri).AbsolutePath;

    protected override async Task OnParametersSetAsync()
    {
        IsViewMode = Navigation.Uri.Contains("/view", StringComparison.OrdinalIgnoreCase);

        var identity = LoadIdentity();
        if (_loadedForPath == identity)
            return;
        _loadedForPath = identity;

        IsLoading = true;
        GlobalError = null;
        SuccessMsg = null;

        // A throw out of LoadAsync (or a page's OnLoadedAsync hook fetching picker data) would
        // otherwise propagate out of OnParametersSetAsync and leave IsLoading pinned true — the
        // edit page then shows its loading spinner forever instead of the form or an error.
        // Every edit page in the app derives from this base. Surface the real failure (not a
        // generic message) so a broken picker/list endpoint is diagnosable rather than showing an
        // empty dropdown with no explanation.
        try
        {
            await LoadAsync();
            CaptureBaseline();
        }
        catch (Exception ex)
        {
            GlobalError = $"Failed to load this page: {ex.Message}";
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            IsLoading = false;
        }
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

            // OnSavedAsync often does a forceLoad navigation (a real browser unload), and
            // <NavigationLock ConfirmExternalNavigation="@HasUnsavedChanges" /> arms the
            // browser's native beforeunload prompt off that same flag. Clearing the flag above
            // only queues a re-render; without giving that render a chance to reach the client
            // first, the browser can still unload with its old (dirty) listener attached and
            // show "Changes that you made may not be saved" even though the save just succeeded.
            StateHasChanged();
            await Task.Delay(50);

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
    // success banner (multi-section pages like CompanyEdit that don't set ListUrl). Routed
    // through NavigateToList() (rather than a raw Navigation.NavigateTo call) so it sets the same
    // _navigatedThisSave flag NavigateToList() always sets — see ConfirmSaveAndCloseAsync's
    // remarks on why that flag exists.
    protected virtual Task OnSavedAsync()
    {
        if (TargetListUrl is not null)
            NavigateToList();
        else
            SuccessMsg = "Saved successfully.";

        return Task.CompletedTask;
    }

    // Wired to the Close button. Prompts to save first if there are unsaved changes.
    protected void RequestClose()
    {
        // Guard against a second trigger (e.g. a rapid double click on the Close button) opening
        // a duplicate dialog instance while one is already open — mirrors
        // HandleLocationChangingAsync's `|| ShowUnsavedChangesDialog` guard above.
        if (ShowUnsavedChangesDialog)
            return;

        if (HasUnsavedChanges)
            ShowUnsavedChangesDialog = true;
        else
            NavigateToList();
    }

    // Set by NavigateToList() every time it actually issues a navigation this "save" cycle —
    // reset at the start of ConfirmSaveAndCloseAsync. Guards against a double forceLoad
    // navigation: SaveAsync's default OnSavedAsync already navigates via NavigateToList() when
    // TargetListUrl is set, so ConfirmSaveAndCloseAsync must not blindly call NavigateToList()
    // again afterwards — two back-to-back forceLoad navigations to (usually) the same URL was
    // producing an extra browser history entry, which is what made the in-app "back" button
    // require two clicks to leave the page instead of one.
    private bool _navigatedThisSave;

    // Navigates to wherever the in-progress navigation attempt was headed
    // (HandleLocationChangingAsync), or ListUrl when the dialog was raised by the Close
    // button instead (no intercepted navigation, so _pendingNavigationUri is null).
    protected void NavigateToList()
    {
        var target = _pendingNavigationUri ?? TargetListUrl;
        _pendingNavigationUri = null;

        if (target is not null)
        {
            // forceLoad: true for the same reason OnSavedAsync's default already uses it — a
            // client-side-only Blazor route change never fires a browser "load" event, which
            // E2E callers (Playwright's WaitForURLAsync defaults to waitUntil: "Load") wait on
            // after Discard/Cancel just as they do after a successful Save.
            _navigationConfirmed = true;
            _navigatedThisSave = true;
            Navigation.NavigateTo(target, forceLoad: true);
        }
    }

    // Save, then always navigate on success — even for pages whose normal OnSavedAsync stays
    // put (e.g. an orchestrator page showing an inline success banner), since the user
    // explicitly chose "Save" from the "unsaved changes" prompt. Skips the extra NavigateToList()
    // call when OnSavedAsync's own default path already navigated (see _navigatedThisSave) to
    // avoid issuing two forceLoad navigations back-to-back.
    protected async Task ConfirmSaveAndCloseAsync()
    {
        ShowUnsavedChangesDialog = false;
        _navigatedThisSave = false;
        await SaveAsync();

        if (GlobalError is null && !_navigatedThisSave)
            NavigateToList();
    }

    protected void DiscardChangesAndClose()
    {
        ShowUnsavedChangesDialog = false;

        // CaptureBaseline() so HasUnsavedChanges reads false before NavigateToList()'s forceLoad
        // navigate fires. <NavigationLock ConfirmExternalNavigation="@HasUnsavedChanges" /> reads
        // that flag's last-rendered value at the moment the browser navigation is issued — left
        // true (the previous assumption being "it doesn't matter, this component is about to be
        // destroyed by an in-app SPA nav anyway"), it fires an unnecessary beforeunload guard on a
        // navigation the user already explicitly confirmed by clicking "Discard Changes". This
        // mirrors SaveAsync's own StateHasChanged()+delay before its forceLoad navigate (see that
        // method's remarks) — the render clearing HasUnsavedChanges needs a chance to reach the
        // client BEFORE the forceLoad fires, or the browser can still show its native "leave
        // site?" prompt on top of (or instead of) our own confirm dialog, which is exactly the
        // double-dialog bug this fixes.
        CaptureBaseline();
        ResetChildSectionsUnsavedState();

        // Only Save should ever validate — Close/Discard must not. Nothing in this class calls
        // EditContext.Validate() from here, but EditPageBase<TModel> owns an EditContext whose
        // per-field CSS state (modified/invalid) is driven by field-level notifications that can
        // accumulate independently of an explicit Validate() call (e.g. Syncfusion inputs raising
        // OnFieldChanged as the user tabs through an empty "Add" form before ever clicking Save).
        // Clearing that per-field "modified" state here guarantees Close never leaves stale
        // validation styling visible on the page being navigated away from, regardless of how it
        // got there.
        ClearEditContextModifiedState();

        StateHasChanged();
        _ = DelayThenNavigateToListAsync();
    }

    private async Task DelayThenNavigateToListAsync()
    {
        await Task.Delay(50);
        NavigateToList();
    }

    // Overridden by EditPageBase<TModel> to call EditContext.MarkAsUnmodified(). No-op for
    // orchestrator pages with no owned EditContext.
    protected virtual void ClearEditContextModifiedState() { }

    // Hook for pages whose HasUnsavedChanges also folds in a child EditSectionBase (e.g.
    // EmployeeEdit's Employment tab) — CaptureBaseline() above only resets this page's OWN model,
    // not a child section's independently-tracked one. Override to call ResetBaseline() on each
    // such child.
    protected virtual void ResetChildSectionsUnsavedState() { }

    protected void CancelCloseDialog()
    {
        ShowUnsavedChangesDialog = false;
        _pendingNavigationUri = null;
    }

    public void Dispose() => _locationChangingRegistration?.Dispose();
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

    protected override void ClearEditContextModifiedState() => EditContext.MarkAsUnmodified();
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
