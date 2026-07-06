using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Inputs;

namespace HR.Web.Components.Controls;

/// <summary>
/// Platform-standard text box. Defaults to FloatLabelType.Auto so every
/// usage automatically gets the floating-label appearance without having
/// to set it at every call site.
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
}
