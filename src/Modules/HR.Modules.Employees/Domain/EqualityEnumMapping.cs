namespace HR.Modules.Employees.Domain;

internal static class EqualityEnumMapping
{
    public static string? ToStored<TEnum>(TEnum? value) where TEnum : struct, Enum
        => value?.ToString();

    public static TEnum? FromStored<TEnum>(string? stored) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(stored, ignoreCase: false, out var parsed) ? parsed : null;
}
