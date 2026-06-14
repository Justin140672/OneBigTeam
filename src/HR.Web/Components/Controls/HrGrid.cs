using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Grids;

namespace HR.Web.Components.Controls;

/// <summary>
/// Platform-standard data grid. Defaults AllowPaging, AllowSorting, and
/// AllowFiltering to true so all call sites get consistent behaviour
/// without repeating the same three attributes everywhere.
/// </summary>
public class HrGrid<TValue> : SfGrid<TValue>
{
    public override Task SetParametersAsync(ParameterView parameters)
    {
        if (!parameters.TryGetValue<bool>(nameof(AllowPaging), out _))
            AllowPaging = true;

        if (!parameters.TryGetValue<bool>(nameof(AllowSorting), out _))
            AllowSorting = true;

        if (!parameters.TryGetValue<bool>(nameof(AllowFiltering), out _))
            AllowFiltering = true;

        if (!parameters.TryGetValue<bool>(nameof(AllowExcelExport), out _))
            AllowExcelExport = true;

        if (!parameters.TryGetValue<bool>(nameof(AllowPdfExport), out _))
            AllowPdfExport = true;

        if (!parameters.TryGetValue<bool>(nameof(ShowColumnChooser), out _))
            ShowColumnChooser = true;

        return base.SetParametersAsync(parameters);
    }
}
