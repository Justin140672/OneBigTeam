using HR.Web.Components.Controls;
using Microsoft.AspNetCore.Components;
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

    private bool _hasSelection;

    private record ToolbarAction(string Id, string Text, string Icon, Func<TItem, Task> OnClick, string? Tooltip = null);
    private readonly List<ToolbarAction> _customActions = new();

    protected void AddToolbarAction(string id, string text, string icon, Func<TItem, Task> onClick, string? tooltip = null)
        => _customActions.Add(new(id, text, icon, onClick, tooltip));

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
                new ItemModel { Id = "hr-add",  Text = "Add",  PrefixIcon = "e-icons e-add",  TooltipText = "Add" },
                new ItemModel { Id = "hr-edit", Text = "Edit", PrefixIcon = "e-icons e-edit", TooltipText = "Edit selected", Disabled = !_hasSelection },
                new ItemModel { Id = "hr-view", Text = "View", PrefixIcon = "e-icons e-eye",  TooltipText = "View selected", Disabled = !_hasSelection },
            };

            foreach (var action in _customActions)
                items.Add(new ItemModel
                {
                    Id          = action.Id,
                    Text        = action.Text,
                    PrefixIcon  = action.Icon,
                    TooltipText = action.Tooltip ?? action.Text,
                    Disabled    = !_hasSelection,
                });

            items.AddRange(new object[] { "Print", "ExcelExport", "CsvExport", "PdfExport", "ColumnChooser" });
            return items;
        }
    }

    protected virtual string? GetAddUrl() => null;
    protected virtual string? GetEditUrl(TItem item) => null;
    protected virtual string? GetViewUrl(TItem item) => null;

    protected void OnRowSelected(RowSelectEventArgs<TItem> args)
    {
        _hasSelection = true;
        StateHasChanged();
    }

    protected void OnRowDeselected(RowDeselectEventArgs<TItem> args)
    {
        _hasSelection = false;
        StateHasChanged();
    }

    protected async Task OnToolbarClick(ClickEventArgs args)
    {
        switch (args.Item.Id)
        {
            case "hr-add":
                var addUrl = GetAddUrl();
                if (addUrl is not null) Navigation.NavigateTo(addUrl);
                break;

            case "hr-edit":
            case "hr-view":
                if (Grid is null || !_hasSelection) break;
                var records = await Grid.GetSelectedRecordsAsync();
                if (records.Count == 0) break;
                var url = args.Item.Id == "hr-edit" ? GetEditUrl(records[0]) : GetViewUrl(records[0]);
                if (url is not null) Navigation.NavigateTo(url);
                break;

            // Built-in toolbar item IDs carry a grid-ID prefix: "{gridId}_excelexport" etc.
            case var id when id.EndsWith("_excelexport", StringComparison.OrdinalIgnoreCase):
                if (Grid is not null) await Grid.ExportToExcelAsync(new ExcelExportProperties());
                break;

            case var id when id.EndsWith("_csvexport", StringComparison.OrdinalIgnoreCase):
                if (Grid is not null) await Grid.ExportToCsvAsync(new ExcelExportProperties());
                break;

            case var id when id.EndsWith("_pdfexport", StringComparison.OrdinalIgnoreCase):
                if (Grid is not null) await Grid.ExportToPdfAsync(new PdfExportProperties());
                break;

            // Print and ColumnChooser are handled client-side by Syncfusion.
            // Fall through to check registered custom actions.
            default:
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

    protected override async Task OnParametersSetAsync()
    {
        await OnBeforeLoadAsync();
        await LoadAsync();
    }

    protected virtual Task OnBeforeLoadAsync() => Task.CompletedTask;

    protected abstract Task<IReadOnlyList<TItem>?> FetchItemsAsync(string? search);

    protected virtual string LoadErrorMessage => "Failed to load data.";

    protected async Task LoadAsync()
    {
        IsLoading = true;
        Error = null;
        StateHasChanged();

        var search = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm;
        var result = await FetchItemsAsync(search);

        if (result is null)
            Error = LoadErrorMessage;
        else
            Items = result;

        IsLoading = false;
    }

    protected async Task OnSearchChanged(string value)
    {
        SearchTerm = value;

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
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }
}
