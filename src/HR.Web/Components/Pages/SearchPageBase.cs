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

    protected virtual bool SupportsActiveFilter => false;

    protected bool ShowInactive { get; private set; }

    protected bool _hasSelection;

    protected int SelectedCount { get; private set; }

    protected virtual string AddButtonText => "Add";

    private record ToolbarAction(string Id, string Text, string Icon, Func<TItem, Task> OnClick, string? Tooltip = null, bool SelectionDependent = true, Func<int, string>? TextWithSelectionCount = null);
    private readonly List<ToolbarAction> _customActions = new();

    protected void AddToolbarAction(string id, string text, string icon, Func<TItem, Task> onClick, string? tooltip = null, bool selectionDependent = true, Func<int, string>? textWithSelectionCount = null)
        => _customActions.Add(new(id, text, icon, onClick, tooltip, selectionDependent, textWithSelectionCount));

    protected virtual void ConfigureToolbar() { }

    protected override void OnInitialized()
    {
        ConfigureToolbar();
        base.OnInitialized();
    }

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
                    Text        = action.TextWithSelectionCount?.Invoke(SelectedCount) ?? action.Text,
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

    protected virtual bool IsAddDisabled => false;

    protected virtual string? GetAddUrl() => null;
    protected virtual string? GetEditUrl(TItem item) => null;
    protected virtual string? GetViewUrl(TItem item) => null;

    protected virtual Task OnViewSelectedAsync(TItem item)
    {
        var url = GetViewUrl(item);
        if (url is not null)
            Navigation.NavigateTo(AppendReturnUrl(url, CurrentListRelativeUrlWithQuery()));
        return Task.CompletedTask;
    }

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

        StateHasChanged();
    }

    protected async Task OnRowDeselected(RowDeselectEventArgs<TItem> args)
    {
        if (Grid is null) return;

        var remaining = await Grid.GetSelectedRecordsAsync();
        _hasSelection = remaining.Count > 0;
        SelectedCount = remaining.Count;
        await Grid.EnableToolbarItemsAsync(SelectionDependentToolbarIds, _hasSelection);

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

    private bool _filterStateRestored;

    private string? _pendingSelfNavigationTarget;

    protected override async Task OnParametersSetAsync()
    {
        if (_pendingSelfNavigationTarget is not null)
        {
            var current = Navigation.ToAbsoluteUri(Navigation.Uri).PathAndQuery;
            var isOwnSync = string.Equals(current, _pendingSelfNavigationTarget, StringComparison.Ordinal);
            _pendingSelfNavigationTarget = null;
            if (isOwnSync)
                return;
        }

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

    private void SyncFilterStateToUrl()
    {
        var target = ListPathWithFilterQuery();
        var current = Navigation.ToAbsoluteUri(Navigation.Uri).PathAndQuery;

        if (!string.Equals(target, current, StringComparison.Ordinal))
        {
            _pendingSelfNavigationTarget = target;
            Navigation.NavigateTo(target, replace: true);
        }
    }

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

    protected virtual TimeSpan? LoadTimeout => null;

    private int _loadGeneration;

    protected virtual void OnLoadCompleted(bool success) { }

    protected async Task LoadAsync()
    {
        var generation = ++_loadGeneration;

        IsLoading = true;
        Error = null;
        StateHasChanged();

        var search = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm;

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
                return;

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
        if (string.Equals((value ?? string.Empty).Trim(), (SearchTerm ?? string.Empty).Trim(), StringComparison.Ordinal))
            return;

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
        _loadGeneration++;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }
}
