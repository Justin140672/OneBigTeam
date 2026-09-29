namespace HR.SharedKernel;

/// <summary>
/// Ticket 19: single, explicit string-normalization convention for user-entered form values
/// crossing an HTTP service boundary (HR.Web and HR.Admin.Web share this). Kept deliberately
/// small, field-aware and free of Blazor/HTTP concerns so it can be unit tested in isolation.
///
/// Do not use this helper for passwords, access tokens, secrets, rich/formatted content, file
/// names, uploaded content, or any field where surrounding whitespace is intentionally meaningful
/// — those cases must be left untouched (or normalized by a feature-specific rule) and the
/// exception documented at the call site.
///
/// Lives in HR.SharedKernel (rather than HR.Web.Utilities) because both HR.Web and
/// HR.Admin.Web already reference this project; duplicating the helper or adding a
/// project reference from HR.Admin.Web to HR.Web would be an unnecessary/awkward coupling.
/// </summary>
public static class FormText
{
    public static string Required(string value) => value.Trim();

    public static string? Optional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string? OptionalSearch(string? value) => Optional(value);
}
