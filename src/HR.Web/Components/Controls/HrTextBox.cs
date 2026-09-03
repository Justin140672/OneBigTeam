using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Inputs;

namespace HR.Web.Components.Controls;

/// <summary>
/// Platform-standard text box. Defaults to FloatLabelType.Never — Syncfusion's Auto/Always modes
/// render the Placeholder text as a floating &lt;label&gt; element instead of the native HTML
/// `placeholder` attribute, which breaks Playwright's GetByPlaceholder() locator used throughout
/// HR.Web.E2E.Tests. A prior change here defaulted to Auto app-wide (59 usages) for floating-label
/// polish and broke GetByPlaceholder-based E2E tests across many unrelated pages (e.g.
/// AssetCategoryEditCloseBehaviorTests) — reverted; opt into FloatLabelType.Auto per call site
/// instead where the floating-label look is actually wanted.
///
/// Also commits the typed value into the bound <c>Value</c> on every keystroke, not only on
/// change/blur (stock <see cref="SfTextBox"/> behaviour — see <see cref="InputHandler"/> below).
/// </summary>
public class HrTextBox : SfTextBox
{
    public override Task SetParametersAsync(ParameterView parameters)
    {
        if (!parameters.TryGetValue<FloatLabelType>(nameof(FloatLabelType), out _))
        {
            FloatLabelType = FloatLabelType.Never;
        }

        CssClass = parameters.TryGetValue<string>(nameof(CssClass), out var cssClass) && !string.IsNullOrWhiteSpace(cssClass)
            ? $"hr-textbox {cssClass}"
            : "hr-textbox";

        return base.SetParametersAsync(parameters);
    }

    /// <summary>
    /// Stock <see cref="SfTextBox"/> only pushes the typed text into the two-way-bound
    /// <c>Value</c> on its ChangeHandler — i.e. on the native <c>change</c> event, which fires on
    /// blur. Anything that reads the bound value before the field loses focus therefore sees the
    /// stale value: every E2E page-object <c>FillAsync</c> (forcing an explicit Tab/blur after
    /// each fill), and a handful of real flows that react to a value mid-edit. Commit the value on
    /// every keystroke as well.
    ///
    /// base.InputHandler still runs first, so Syncfusion's own internal state and the
    /// <see cref="SfTextBox.Input"/> callback (a call site's own per-keystroke handler, e.g.
    /// Login.razor clearing its error banner) are untouched. The blur-time ChangeHandler also
    /// still runs — it re-raises ValueChanged with the same value (a no-op bind) and is what
    /// notifies the EditContext, so validation still surfaces on blur/submit exactly as before.
    /// </summary>
    protected override async Task InputHandler(ChangeEventArgs args)
    {
        await base.InputHandler(args);

        var typed = args?.Value?.ToString() ?? string.Empty;
        if (!string.Equals(Value, typed, StringComparison.Ordinal))
        {
            Value = typed;
            if (ValueChanged.HasDelegate)
            {
                await ValueChanged.InvokeAsync(typed);
            }
        }
    }
}
