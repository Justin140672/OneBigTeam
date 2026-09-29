using System.Text;

namespace HR.Web.Components.Controls;

public static class EnumDisplay
{
    public static string Humanize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var sb = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (i > 0 && char.IsUpper(c) &&
                (char.IsLower(value[i - 1]) || (i + 1 < value.Length && char.IsLower(value[i + 1]) && char.IsUpper(value[i - 1]))))
            {
                sb.Append(' ');
            }
            sb.Append(c);
        }

        return sb.ToString();
    }

    public static string Humanize(Enum value) => Humanize(value.ToString());
}
