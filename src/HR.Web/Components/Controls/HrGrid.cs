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

        // SfGrid has no CssClass parameter of its own — the "class" attribute flows through
        // UnMatchedAttributes instead, so it has to be merged into the parameter set itself
        // rather than assigned as a property. Stable marker class so app.css can apply
        // consistent header/row/focus styling to every grid in the app from one place.
        var attributes = new Dictionary<string, object?>(parameters.ToDictionary());
        var existingClass = attributes.TryGetValue("class", out var value) ? value?.ToString() : null;
        attributes["class"] = string.IsNullOrWhiteSpace(existingClass) ? "hr-grid" : $"hr-grid {existingClass}";

        return base.SetParametersAsync(ParameterView.FromDictionary(attributes));
    }
}
