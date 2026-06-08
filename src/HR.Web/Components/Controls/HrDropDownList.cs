using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.DropDowns;
using Syncfusion.Blazor.Inputs;

namespace HR.Web.Components.Controls;

/// <summary>
/// Platform-standard drop-down list. Defaults to FloatLabelType.Auto.
/// </summary>
public class HrDropDownList<TValue, TItem> : SfDropDownList<TValue, TItem>
{
    public override Task SetParametersAsync(ParameterView parameters)
    {
        if (!parameters.TryGetValue<FloatLabelType>(nameof(FloatLabelType), out _))
        {
            FloatLabelType = FloatLabelType.Auto;
        }

        return base.SetParametersAsync(parameters);
    }
}
