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
/// </summary>
public class HrTextBox : SfTextBox
{
    // NOTE: an InputHandler override that raised ValueChanged on every keystroke was tried (to
    // remove the "Tab after FillAsync" dance from the E2E page objects). On Blazor Server that
    // makes each keystroke round-trip and re-render the controlled input, so fast typing loses
    // characters / jumps the cursor / "the field won't type" (Add External Recruiter, Add
    // Recruitment Stage, …). The page objects already blur after filling, so the override bought
    // nothing — removed. Stock SfTextBox commits on change/blur, which is correct here.

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
}
