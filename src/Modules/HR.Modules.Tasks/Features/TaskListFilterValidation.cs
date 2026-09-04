namespace HR.Modules.Tasks.Features;

/// <summary>
/// Shared validation predicates for the string-based status/priority filters used by the
/// task-list endpoints (My tasks, Employee tasks, Team tasks). An invalid filter value must be
/// rejected with a 422 before the handler / database is touched — silently ignoring it (the old
/// <c>Enum.TryParse</c>-and-skip behaviour) let <c>status=Compeleted</c> return every task.
/// </summary>
internal static class TaskListFilterValidation
{
    /// <summary>
    /// True when the value is null/blank (no filter) or an exact, case-insensitive match for a
    /// declared name of <typeparamref name="TEnum"/>. Numeric strings and undefined values are
    /// rejected, unlike a bare <see cref="System.Enum.TryParse{TEnum}(string, bool, out TEnum)"/>.
    /// </summary>
    public static bool IsValidOptionalFilter<TEnum>(string? value)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        foreach (var name in Enum.GetNames<TEnum>())
        {
            if (string.Equals(name, value.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string AllowedValuesMessage<TEnum>(string filterName)
        where TEnum : struct, Enum
        => $"{filterName} must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.";
}
