using Syncfusion.Blazor.Calendars;

namespace HR.Web.Components.Controls;

public class HrDatePicker<TValue> : SfDatePicker<TValue>
{
    private static readonly string[] DefaultInputFormats =
        ["d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "d-M-yyyy", "d.M.yyyy", "ddMMyyyy"];

    protected override void OnInitialized()
    {
        Format ??= "dd/MM/yyyy";
        InputFormats ??= DefaultInputFormats;
        base.OnInitialized();
    }
}
