namespace HR.SharedKernel;

public static class PersonName
{
    public static string Display(string? firstName, string? lastName, string? preferredName) =>
        Join(IsPreferredDistinct(firstName, preferredName) ? preferredName!.Trim() : firstName, lastName);

    public static string Legal(string? firstName, string? lastName) => Join(firstName, lastName);

    public static bool IsPreferredDistinct(string? firstName, string? preferredName) =>
        HasText(preferredName)
        && !string.Equals(preferredName!.Trim(), firstName?.Trim(), StringComparison.OrdinalIgnoreCase);

    public static string DisplayWithLegal(string? firstName, string? lastName, string? preferredName) =>
        IsPreferredDistinct(firstName, preferredName)
            ? $"{Display(firstName, lastName, preferredName)} (legal name: {Legal(firstName, lastName)})"
            : Display(firstName, lastName, preferredName);

    public static string Initials(string? firstName, string? lastName, string? preferredName)
    {
        var first = IsPreferredDistinct(firstName, preferredName) ? preferredName : firstName;
        return $"{first?.Trim().FirstOrDefault()}{lastName?.Trim().FirstOrDefault()}".ToUpperInvariant();
    }

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string Join(string? first, string? last) => $"{first?.Trim()} {last?.Trim()}".Trim();
}
