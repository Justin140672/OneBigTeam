using HR.Web.Components.Controls;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.WebUtilities;
using Syncfusion.Blazor.Grids;
using Syncfusion.Blazor.Navigations;

namespace HR.Web.Components.Pages;

public abstract class SearchPageBase<TItem> : ComponentBase, IDisposable
{
    [Parameter] public Guid CompanyId { get; set; }

    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    protected bool IsLoading { get; private set; } = true;
    protected string? Error { get; private set; }
    protected string? ActionError { get; private set; }
    protected string SearchTerm { get; private set; } = string.Empty;
    protected IReadOnlyList<TItem> Items { get; private set; } = [];

    protected HrGrid<TItem>? Grid { get; set; }

    // Override to true for entities that have an IsActive property, to show the toggle below.
    protected virtual bool SupportsActiveFilter => false;

    protected bool ShowInactive { get; private set; }

    protected bool _hasSelection;

    // Number of rows currently checked in the grid's checkbox-selection column. Kept alongside
    // _hasSelection (rather than replacing it) so existing callers that only care about "is
    // anything selected" are unaffected; pages that want to show the count (e.g. EmployeeList's
    // "Update selected (N)" bulk-action label) can read this directly.
    protected int SelectedCount { get; private set; }

    // Override to customise the "Add" toolbar button's label — e.g. EmployeeList uses
    // "Add employee" so the primary action reads unambiguously rather than a bare "Add".
    protected virtual string AddButtonText => "Add";

    private record ToolbarAction(string Id, string Text, string Icon, Func<TItem, Task> OnClick, string? Tooltip = null, bool SelectionDependent = true);
    private readonly List<ToolbarAction> _customActions = new();

    /// <param name="selectionDependent">
    /// Whether this action should start disabled and only enable once a row is selected (the
    /// default, matching every action that operates on the current selection). Pass false for an
    /// action that doesn't need a selection at all — e.g. a dropdown covering several actions
    /// where only some of them are selection-dependent (see EmployeeList's "Bulk Update" menu,
    /// which also offers "Import"/"Download Template" alongside "Selected Employees").
    /// </param>
    protected void AddToolbarAction(string id, string text, string icon, Func<TItem, Task> onClick, string? tooltip = null, bool selectionDependent = true)
        => _customActions.Add(new(id, text, icon, onClick, tooltip, selectionDependent));

    // Override to register custom toolbar actions via AddToolbarAction.
    protected virtual void ConfigureToolbar() { }

    protected override void OnInitialized()
    {
        ConfigureToolbar();
        base.OnInitialized();
    }

    // Recomputed on every render so Disabled reflects current selection state.
    protected IEnumerable<object> GridToolbar
    {
        get
        {
            var items = new List<object>
            {
                new ItemModel { Id = "hr-add",  Text = AddButtonText,  PrefixIcon = "fa-solid fa-plus", TooltipText = AddButtonText, Disabled = IsAddDisabled },
                new ItemModel { Id = "hr-edit", Text = "Edit", PrefixIcon = "fa-solid fa-pen",  TooltipText = "Edit selected", Disabled = !_hasSelection },
                new ItemModel { Id = "hr-view", Text = "View", PrefixIcon = "fa-solid fa-eye",  TooltipText = "View selected", Disabled = !_hasSelection },
            };

            foreach (var action in _customActions)
                items.Add(new ItemModel
                {
                    Id          = action.Id,
                    Text        = action.Text,
                    PrefixIcon  = action.Icon,
                    TooltipText = action.Tooltip ?? action.Text,
                    Disabled    = action.SelectionDependent && !_hasSelection,
                });

            if (SupportsActiveFilter)
                items.Add(new ItemModel
                {
                    Id          = "hr-toggle-active",
                    Text        = ShowInactive ? "Show Active" : "Show Inactive",
                    PrefixIcon  = ShowInactive ? "fa-solid fa-eye" : "fa-solid fa-eye-slash",
                    TooltipText = ShowInactive ? "Show active records only" : "Include inactive records",
                });

            items.AddRange(new object[]
            {
                new ItemModel { Id = "hr-print",   Text = "Print",   PrefixIcon = "fa-solid fa-print", TooltipText = "Print" },
                new ItemModel { Id = "hr-export",  Type = ItemType.Input, Template = ExportMenuTemplate, TooltipText = "Export" },
                new ItemModel { Id = "hr-columns", Text = "Columns", PrefixIcon = "fa-solid fa-table-columns", TooltipText = "Show/hide columns" },
            });
            return items;
        }
    }

    // Mounted into the toolbar via an ItemModel.Template slot; merges Excel/CSV/PDF into one dropdown.
    // Print stays as its own native toolbar button (see "hr-print" in OnToolbarClick).
    private RenderFragment ExportMenuTemplate => builder =>
    {
        builder.OpenComponent<ExportMenu>(0);
        builder.AddAttribute(1, nameof(ExportMenu.OnItemSelected), EventCallback.Factory.Create<string>(this, HandleExportItemSelected));
        builder.CloseComponent();
    };

    private async Task HandleExportItemSelected(string id)
    {
        if (Grid is null) return;

        switch (id)
        {
            case "hr-excel":
                await Grid.ExportToExcelAsync(new ExcelExportProperties());
                break;

            case "hr-csv":
                await Grid.ExportToCsvAsync(new ExcelExportProperties());
                break;

            case "hr-pdf":
                await Grid.ExportToPdfAsync(new PdfExportProperties());
                break;
        }
    }

    // Override (e.g. => Session.IsReadOnly) on pages where the "Add" action should be hidden
    // while the company's subscription is read-only. Defense-in-depth only — the real
    // enforcement is server-side; not every list page opts into this yet.
    protected virtual bool IsAddDisabled => false;

    protected virtual string? GetAddUrl() => null;
    protected virtual string? GetEditUrl(TItem item) => null;
    protected virtual string? GetViewUrl(TItem item) => null;

    // Default "View" action navigates to GetViewUrl(item). Override to do something else instead
    // (e.g. open a dialog) — see AssetList for an example.
    protected virtual Task OnViewSelectedAsync(TItem item)
    {
        var url = GetViewUrl(item);
        if (url is not null)
            Navigation.NavigateTo(AppendReturnUrl(url, CurrentListRelativeUrlWithQuery()));
        return Task.CompletedTask;
    }

    // Ids of toolbar buttons whose enabled state tracks row selection. Re-assigning the
    // Toolbar parameter (via GridToolbar) only sets the *initial* Disabled state when the
    // grid's underlying JS toolbar is first created — Syncfusion Blazor Grid doesn't refresh
    // an already-rendered toolbar just because a new Toolbar object was bound, so selection
    // changes after first render need this explicit interop call to actually update the DOM.
    private List<string> SelectionDependentToolbarIds =>
        new List<string> { "hr-edit", "hr-view" }
            .Concat(_customActions.Where(a => a.SelectionDependent).Select(a => a.Id))
            .ToList();

    protected async Task OnRowSelected(RowSelectEventArgs<TItem> args)
    {
        _hasSelection = true;
        if (Grid is not null)
        {
            await Grid.EnableToolbarItemsAsync(SelectionDependentToolbarIds, true);
            SelectedCount = (await Grid.GetSelectedRecordsAsync()).Count;
        }

        // This handler is invoked directly by the SfGrid component's own EventCallback
        // dispatch (a component event, not a native DOM UI event routed through Blazor's
        // renderer), so nothing triggers a re-render automatically afterwards. Without this,
        // SelectedCount/_hasSelection update in memory but the "Update selected (N)" button
        // template (BulkUpdateMenuTemplate) never repaints to reflect it.
        StateHasChanged();
    }

    protected async Task OnRowDeselected(RowDeselectEventArgs<TItem> args)
    {
        if (Grid is null) return;

        // On a multi-select grid, deselecting one row doesn't necessarily mean nothing is
        // selected anymore — check what's actually still selected rather than assuming zero
        // (which only happened to be safe back when every grid using this base class was
        // single-select, where deselecting the one selected row always left none behind).
        var remaining = await Grid.GetSelectedRecordsAsync();
        _hasSelection = remaining.Count > 0;
        SelectedCount = remaining.Count;
        await Grid.EnableToolbarItemsAsync(SelectionDependentToolbarIds, _hasSelection);

        // See comment in OnRowSelected — same missing-render issue applies here too.
        StateHasChanged();
    }

    protected async Task OnToolbarClick(ClickEventArgs args)
    {
        switch (args.Item.Id)
        {
            case "hr-add":
                if (IsAddDisabled) break;
                var addUrl = GetAddUrl();
                if (addUrl is not null)
                    Navigation.NavigateTo(AppendReturnUrl(addUrl, CurrentListRelativeUrlWithQuery()));
                break;

            case "hr-edit":
            case "hr-view":
                if (Grid is null || !_hasSelection) break;
                var records = await Grid.GetSelectedRecordsAsync();
                if (records.Count == 0) break;
                if (args.Item.Id == "hr-edit")
                {
                    var url = GetEditUrl(records[0]);
                    if (url is not null)
                        Navigation.NavigateTo(AppendReturnUrl(url, CurrentListRelativeUrlWithQuery()));
                }
                else
                {
                    await OnViewSelectedAsync(records[0]);
                }
                break;

            case "hr-print":
                if (Grid is not null) await Grid.PrintAsync();
                break;

            case "hr-columns":
                if (Grid is not null) await Grid.OpenColumnChooserAsync(0, 0);
                break;

            case "hr-toggle-active":
                ShowInactive = !ShowInactive;
                SyncFilterStateToUrl();
                await LoadAsync();
                break;

            default:
                // Skip items whose toolbar slot has been overridden with a Template (Type ==
                // ItemType.Input) — e.g. EmployeeList's EmployeeToolbar swaps the plain
                // "hr-bulk-update" button for a BulkUpdateMenu dropdown Template that manages its
                // own click/selection flow via its own ItemSelected event. Without this guard, a
                // single click on such a templated toolbar item both opens its own popup/dropdown
                // AND bubbles up as a native grid toolbar click here, double-firing the registered
                // customAction.OnClick (e.g. immediately opening BulkCompensationUpdateDialog while
                // the BulkUpdateMenu dropdown is still open), which caused the dialog to render on
                // top of — and intercept pointer events for — the still-open dropdown menu.
                if (args.Item.Type == ItemType.Input)
                    break;

                var customAction = _customActions.FirstOrDefault(a => a.Id == args.Item.Id);
                if (customAction is not null && _hasSelection && Grid is not null)
                {
                    var selected = await Grid.GetSelectedRecordsAsync();
                    if (selected.Count > 0)
                        await customAction.OnClick(selected[0]);
                }
                break;
        }
    }

    private CancellationTokenSource? _searchCts;

    // ADM-07 — restore the list's own filter state (search term + inactive toggle) from the URL
    // query string on a direct hit, so a breadcrumb/back link into the exact filtered view works.
    private bool _filterStateRestored;

    protected override async Task OnParametersSetAsync()
    {
        if (!_filterStateRestored)
        {
            _filterStateRestored = true;
            var query = QueryHelpers.ParseQuery(Navigation.ToAbsoluteUri(Navigation.Uri).Query);

            if (query.TryGetValue("q", out var q) && !string.IsNullOrWhiteSpace(q))
                SearchTerm = q.ToString();

            if (query.TryGetValue("inactive", out var inactive) && inactive == "true")
                ShowInactive = true;
        }

        await OnBeforeLoadAsync();
        await LoadAsync();
    }

    // The list's own path with its current filter state encoded as a query string.
    private string ListPathWithFilterQuery()
    {
        var path = Navigation.ToAbsoluteUri(Navigation.Uri).AbsolutePath;
        var parameters = new Dictionary<string, string?>();

        if (!string.IsNullOrWhiteSpace(SearchTerm))
            parameters["q"] = SearchTerm;
        if (ShowInactive)
            parameters["inactive"] = "true";

        return parameters.Count == 0 ? path : QueryHelpers.AddQueryString(path, parameters);
    }

    // Push the current filter state into the browser URL in place (no navigation/history entry),
    // but only when it actually differs — avoids a re-entrancy loop.
    private void SyncFilterStateToUrl()
    {
        var target = ListPathWithFilterQuery();
        var current = Navigation.ToAbsoluteUri(Navigation.Uri).PathAndQuery;

        if (!string.Equals(target, current, StringComparison.Ordinal))
            Navigation.NavigateTo(target, replace: true);
    }

    // The current list URL (path + query), app-relative with a leading slash — passed as
    // "?returnUrl=" so the edit/view page's Close button and breadcrumb return to this exact view.
    private string CurrentListRelativeUrlWithQuery()
    {
        var relative = Navigation.ToBaseRelativePath(Navigation.Uri);
        return relative.StartsWith('/') ? relative : "/" + relative;
    }

    private static string AppendReturnUrl(string target, string returnUrl)
    {
        var separator = target.Contains('?') ? '&' : '?';
        return $"{target}{separator}returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

    protected virtual Task OnBeforeLoadAsync() => Task.CompletedTask;

    protected abstract Task<IReadOnlyList<TItem>?> FetchItemsAsync(string? search);

    protected virtual string LoadErrorMessage => "Failed to load data.";

    // Override to bound how long LoadAsync waits for FetchItemsAsync before treating the load as
    // failed (surfacing the Failed/Retry state instead of hanging on the loading indicator
    // forever). Null (the default) preserves the previous unbounded-wait behaviour for pages that
    // haven't opted in, so this is safe to add without touching every list screen at once.
    protected virtual TimeSpan? LoadTimeout => null;

    // Bumped on every LoadAsync call (including via Dispose) so a response that arrives after a
    // newer load was kicked off — or after the component was disposed/navigated away from — is
    // recognised as stale and never applied to Items/Error.
    private int _loadGeneration;

    // Override to react to a load finishing (success or failure) — e.g. to push a screen-reader
    // announcement. Only invoked for the most recent LoadAsync call, never for a stale/superseded
    // one.
    protected virtual void OnLoadCompleted(bool success) { }

    protected async Task LoadAsync()
    {
        var generation = ++_loadGeneration;

        IsLoading = true;
        Error = null;
        StateHasChanged();

        var search = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm;

        // FetchItemsAsync overrides generally translate an HttpRequestException into a null return
        // (→ Error below), but a non-HttpRequestException (a timeout / TaskCanceledException, a
        // deserialization error) would otherwise propagate out of here and out of
        // OnParametersSetAsync, leaving IsLoading stuck true — the grid then shows its loading
        // spinner forever instead of rows, an empty row, or the error alert. Every list page in
        // the app derives from this base, so that one gap is a broad "the page just never
        // finishes loading" failure mode. Fail into the visible Error state instead.
        try
        {
            var fetchTask = FetchItemsAsync(search);
            var timedOut = false;

            if (LoadTimeout is { } timeout)
            {
                var delayTask = Task.Delay(timeout);
                var completedTask = await Task.WhenAny(fetchTask, delayTask);
                timedOut = completedTask == delayTask;
            }

            if (generation != _loadGeneration)
                return; // superseded by a newer load (retry) or the component was disposed

            if (timedOut)
            {
                Error = LoadErrorMessage;
            }
            else
            {
                var result = await fetchTask;

                if (generation != _loadGeneration)
                    return;

                if (result is null)
                    Error = LoadErrorMessage;
                else
                    Items = result;
            }
        }
        catch (Exception)
        {
            if (generation != _loadGeneration)
                return;

            Error = LoadErrorMessage;
        }
        finally
        {
            if (generation == _loadGeneration)
                IsLoading = false;
        }

        if (generation == _loadGeneration)
            OnLoadCompleted(Error is null);
    }

    protected async Task OnSearchChanged(string value)
    {
        SearchTerm = value;
        SyncFilterStateToUrl();

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            await Task.Delay(300, token);
            await LoadAsync();
        }
        catch (TaskCanceledException) { }
    }

    protected void SetActionError(string? message)
    {
        ActionError = message;
        StateHasChanged();
    }

    protected void ClearActionError() => SetActionError(null);

    public void Dispose()
    {
        // Invalidate any in-flight LoadAsync so a late response (e.g. one that timed out, or is
        // still waiting on a slow backend) can never be applied after the user has navigated away.
        _loadGeneration++;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }
}
