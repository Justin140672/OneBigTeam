using HR.SharedKernel;

namespace HR.Web.Components.Controls;

public static class EnumDisplay
{
    public static string Humanize(string? value) => EnumText.Humanize(value);

    public static string Humanize(Enum? value) => EnumText.Humanize(value);
}
