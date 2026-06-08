using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Inputs;

namespace HR.Web.Components.Controls;

/// <summary>
/// Platform-standard numeric text box. Defaults to FloatLabelType.Auto.
/// </summary>
public class HrNumericTextBox<TValue> : SfNumericTextBox<TValue>
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
