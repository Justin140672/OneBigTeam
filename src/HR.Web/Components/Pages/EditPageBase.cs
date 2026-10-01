using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.WebUtilities;
using HR.SharedKernel.Http;
using HR.SharedKernel.Idempotency;
using HR.Web.Services;

namespace HR.Web.Components.Pages;

public abstract class EditPageBase : ComponentBase, IDisposable
{
    [Inject] protected NavigationManager Navigation { get; set; } = default!;
    [Inject] protected AppSession ReadOnlyAppSession { get; set; } = default!;

    protected bool IsSubscriptionReadOnly => ReadOnlyAppSession.IsReadOnly;

    protected bool IsLoading { get; set; } = true;
    protected bool IsViewMode { get; private set; }
    protected bool Saving { get; private set; }
    protected string? GlobalError { get; set; }
    protected string? SuccessMsg { get; set; }
    protected bool ShowUnsavedChangesDialog { get; set; }

    // ── Optimistic concurrency (Ticket 2) ────────────────────────────────────────
    // True when the last save was rejected by the API with HTTP 409 / code "concurrency" (the
    // record, or one of this page's child sections' records, changed elsewhere since it was
    // loaded). A SaveCoreAsync implementation sets this from an ApiSaveResult.IsConcurrencyConflict
    // (or a child EditSectionBase.SaveConflict). Drives the shared <SaveConflictBanner>, which
    // takes precedence over GlobalError in the markup.
    protected bool SaveConflict { get; set; }

    // Ticket 3 (P1) final gap: true when the last save attempt returned MutationOutcomeKind.
    // AmbiguousFailure (a 5xx/408/429/transport failure/timeout/malformed response) — the mutation
    // may or may not have committed server-side. The idempotency key is deliberately retained (see
    // EditPageBase<TModel, TKey>.SaveCoreAsync) so an unchanged retry safely replays rather than
    // repeats the mutation. Drives an "unconfirmed, not a definitive failure" banner distinct from
    // GlobalError's normal "this was rejected" styling, and gates the explicit abandon action below.
    protected bool SaveAmbiguous { get; set; }

    // Explicit user action (Ticket 3 final gap item 7) to give up on a pending ambiguous operation —
    // e.g. the user closes the dialog/page and intentionally starts a fresh attempt instead of
    // retrying. Must never be inferred automatically: only a real user choice, after being warned
    // that abandoning may create a second record if the previous attempt actually committed, may
    // discard a retained idempotency key. Concrete pages/dialogs wire this to a "Start Over" action
    // that is only shown while SaveAmbiguous is true.
    protected virtual void AbandonPendingOperation()
    {
        SaveAmbiguous = false;
        GlobalError = null;
    }

    protected async Task ReloadLatestValuesAsync()
    {
        await ReloadServerStateAsync();
        SaveConflict = false;
        GlobalError = null;
        StateHasChanged();
    }

    protected virtual Task ReloadServerStateAsync() => Task.CompletedTask;

    protected virtual string? ListUrl => null;

    protected string? ReturnUrl
    {
        get
        {
            var uri = Navigation.ToAbsoluteUri(Navigation.Uri);
            if (!QueryHelpers.ParseQuery(uri.Query).TryGetValue("returnUrl", out var value))
                return null;

            return ReturnUrlValidator.ValidateInternalPath(value.ToString());
        }
    }

    private string? TargetListUrl => ReturnUrl ?? ListUrl;

    protected virtual bool HasUnsavedChanges => false;

    private string? _pendingNavigationUri;
    private IDisposable? _locationChangingRegistration;

    private bool _navigationConfirmed;

    protected void SuppressNextNavigationGuard() => _navigationConfirmed = true;

    protected override void OnInitialized()
    {
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

    private string? _loadedForPath;

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

    protected virtual void CaptureBaseline() { }

    protected virtual bool Validate() => true;

    protected async Task SaveAsync()
    {
        GlobalError = null;
        SuccessMsg = null;
        SaveConflict = false;
        SaveAmbiguous = false;

        if (!Validate())
        {
            GlobalError = "Please correct the highlighted fields below.";
            return;
        }

        Saving = true;
        StateHasChanged();

        // Bug fix (P1 follow-up to Ticket 3): Saving was previously only cleared once SaveCoreAsync
        // returned normally. IEditService/IIdempotentCreateService implementations (AssetService,
        // LeaveService, ...) no longer let body-read failures escape as exceptions - they're
        // classified as AmbiguousFailure instead - but this try/finally is kept as defence in depth:
        // any other unexpected exception out of SaveCoreAsync would otherwise leave Saving pinned
        // true forever, permanently disabling the Save button with no way to retry or abandon the
        // pending operation.
        string? error;
        try
        {
            error = await SaveCoreAsync();
        }
        catch (Exception)
        {
            SaveAmbiguous = true;
            Saving = false;
            GlobalError = "We could not confirm whether this save was applied. Please try again.";
            return;
        }

        Saving = false;

        if (error is null)
        {
            CaptureBaseline();

            StateHasChanged();
            await Task.Delay(50);

            await OnSavedAsync();
        }
        else
        {
            GlobalError = error;
        }
    }

    protected abstract Task<string?> SaveCoreAsync();

    protected virtual Task OnSavedAsync()
    {
        if (TargetListUrl is not null)
            NavigateToList();
        else
            SuccessMsg = "Saved successfully.";

        return Task.CompletedTask;
    }

    protected void RequestClose()
    {
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

    protected void NavigateToList()
    {
        var target = _pendingNavigationUri ?? TargetListUrl;
        _pendingNavigationUri = null;

        if (target is not null)
        {
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

    protected virtual void ClearEditContextModifiedState() { }

    protected virtual void ResetChildSectionsUnsavedState() { }

    protected void CancelCloseDialog()
    {
        ShowUnsavedChangesDialog = false;
        _pendingNavigationUri = null;
    }

    public virtual void Dispose() => _locationChangingRegistration?.Dispose();
}

public abstract class EditPageBase<TModel> : EditPageBase where TModel : class, new()
{
    protected TModel Model { get; } = new();
    protected EditContext EditContext { get; private set; } = default!;

    private string? _baselineSnapshot;

    protected override void OnInitialized()
    {
        EditContext = new EditContext(Model);
        EditContext.OnValidationStateChanged += OnValidationStateChanged;
        base.OnInitialized();
    }

    public override void Dispose()
    {
        EditContext.OnValidationStateChanged -= OnValidationStateChanged;
        base.Dispose();
    }

    private void OnValidationStateChanged(object? sender, ValidationStateChangedEventArgs e)
    {
        if (SaveConflict && EditContext.GetValidationMessages().Any())
        {
            SaveConflict = false;
            StateHasChanged();
        }
    }

    protected override bool Validate() => EditContext.Validate();

    protected override void CaptureBaseline() =>
        _baselineSnapshot = System.Text.Json.JsonSerializer.Serialize(Model);

    protected override bool HasUnsavedChanges =>
        _baselineSnapshot is not null && _baselineSnapshot != System.Text.Json.JsonSerializer.Serialize(Model);

    protected override void ClearEditContextModifiedState() => EditContext.MarkAsUnmodified();
}

public abstract class EditPageBase<TModel, TKey> : EditPageBase<TModel>
    where TModel : class, new()
    where TKey : struct
{
    protected abstract IEditService<TModel, TKey> Service { get; }
    protected abstract Guid GetCompanyId();
    protected abstract TKey? GetId();

    // Ticket 3 (P1) final follow-up items 1/2: THIS page instance - not the scoped service - owns
    // the idempotency key for the one logical "create" operation it currently represents. A
    // second, independent create (a different page instance - e.g. the user opens a second tab,
    // or navigates away and back) gets its own instance of this class and therefore its own key.
    private readonly PendingIdempotentOperation _createOperation = new();

    protected virtual bool IsNew => GetId() is null;

    // Ticket 3 (P1) final gap item 7: explicit abandonment discards the retained key/fingerprint —
    // the NEXT Save (even of the identical model) is treated as a brand new logical create. Only
    // reachable via a real user action (see base AbandonPendingOperation's remarks) while
    // SaveAmbiguous is true.
    protected override void AbandonPendingOperation()
    {
        _createOperation.Complete();
        base.AbandonPendingOperation();
    }

    // Ticket 2: the optimistic-concurrency token the current Model was loaded at, read straight off
    // the model when it (and its service) opt into concurrency. Sent as the expected version on the
    // next save and refreshed from every successful update — see SaveCoreAsync below.
    protected int? LoadedVersion => Model is IHasVersion v ? v.Version : null;

    protected override async Task LoadAsync()
    {
        if (!IsNew)
        {
            var loaded = await Service.GetByIdAsync(GetCompanyId(), GetId()!.Value);
            if (loaded is not null) CopyProperties(loaded, Model);
        }

        await OnLoadedAsync();
    }

    protected virtual Task OnLoadedAsync() => Task.CompletedTask;

    // Ticket 2: concurrency-conflict recovery for the shared <SaveConflictBanner>. Re-fetches the
    // entity from the server, repopulating Model (including its version) in place.
    protected override async Task ReloadServerStateAsync()
    {
        if (IsNew) return;
        var loaded = await Service.GetByIdAsync(GetCompanyId(), GetId()!.Value);
        if (loaded is not null) CopyProperties(loaded, Model);
        await OnLoadedAsync();
    }

    protected override async Task<string?> SaveCoreAsync()
    {
        if (IsNew)
        {
            if (Service is IIdempotentCreateService<TModel> idempotentService)
            {
                // Bug fix (P1 follow-up to Ticket 19): fingerprint the service's own canonical,
                // normalized outgoing request (via BuildRequestSnapshot) rather than the raw Model -
                // the snapshot already carries its own company/resource scope (e.g. CompanyId is a
                // field on the request DTO), so whitespace-only edits or blank/null differences on
                // optional fields that normalize to the identical HTTP request no longer rotate the
                // key. Fingerprinting the raw Model instead would rotate the key on cosmetic changes
                // that never reach the wire, letting an ambiguous retry create a duplicate.
                var key = _createOperation.PrepareKey(idempotentService.BuildRequestSnapshot(GetCompanyId(), Model));

                var outcome = await idempotentService.CreateAsync(GetCompanyId(), Model, key);

                // Ticket 3 (P1) final gap: only a DEFINITIVE outcome (Succeeded or Rejected)
                // completes this operation and discards its key — an AmbiguousFailure (5xx/408/429/
                // transport failure/timeout/malformed response) must retain the key and snapshot so
                // an unchanged retry replays the server's stored result rather than repeating the
                // create.
                if (outcome.Kind == HR.SharedKernel.Idempotency.MutationOutcomeKind.AmbiguousFailure)
                {
                    SaveAmbiguous = true;
                    return outcome.Error ?? "The request could not be confirmed. It's safe to try again.";
                }

                _createOperation.Complete();
                return outcome.Kind == HR.SharedKernel.Idempotency.MutationOutcomeKind.Succeeded
                    ? null
                    : outcome.Error ?? "Failed to save.";
            }

            var (createdModel, createModelError) = await Service.CreateAsync(GetCompanyId(), Model);
            return createdModel is not null ? null : createModelError ?? "Failed to save.";
        }

        // Ticket 2: when the service round-trips a version, send the loaded token and surface a
        // stale-save 409 as SaveConflict (drives the shared <SaveConflictBanner>).
        if (Service is IConcurrencyAwareEditService<TModel, TKey> concurrencyAware)
        {
            var saveResult = await concurrencyAware.UpdateAsync(
                GetCompanyId(), GetId()!.Value, Model, LoadedVersion);
            SaveConflict = saveResult.IsConcurrencyConflict;
            if (!saveResult.Success)
                return saveResult.ErrorMessage ?? "Failed to save.";
            if (saveResult.NewVersion is { } newVersion && Model is IHasVersion hasVersion)
                hasVersion.Version = newVersion;
            return null;
        }

        var (result, error) = await Service.UpdateAsync(GetCompanyId(), GetId()!.Value, Model);
        return result is not null ? null : error ?? "Failed to save.";
    }

    private static void CopyProperties(TModel source, TModel target)
    {
        foreach (var prop in typeof(TModel).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.CanRead && prop.CanWrite)
                prop.SetValue(target, prop.GetValue(source));
        }
    }
}
