namespace HR.Modules.Employees.Domain;

/// <summary>
/// The <see cref="EmployeeEqualityData"/> entity stores each answer as the string name of its enum
/// member (or null). The enum &lt;-&gt; string translation lives here so handlers stay thin and both
/// directions stay in sync.
/// </summary>
internal static class EqualityEnumMapping
{
    public static string? ToStored<TEnum>(TEnum? value) where TEnum : struct, Enum
        => value?.ToString();

    public static TEnum? FromStored<TEnum>(string? stored) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(stored, ignoreCase: false, out var parsed) ? parsed : null;
}
