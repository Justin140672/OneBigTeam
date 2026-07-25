namespace HR.Modules.Employees.Features.ImportCompensationChanges;

internal enum ImportCompensationOutcomeType
{
    Success,
    InvalidFile,
    ValidationFailed
}

/// <summary>
/// The Result&lt;T&gt; pattern used elsewhere in this codebase carries a single Error message,
/// which isn't expressive enough for "return every row-level error found in the file" — so this
/// import handler returns a small dedicated outcome type instead of Result&lt;T&gt;.
/// </summary>
internal sealed record ImportCompensationChangesOutcome(
    ImportCompensationOutcomeType Type,
    ImportCompensationChangesResponse? Response,
    IReadOnlyList<CompensationImportRowError> RowErrors,
    string? Error)
{
    public static ImportCompensationChangesOutcome Success(ImportCompensationChangesResponse response) =>
        new(ImportCompensationOutcomeType.Success, response, [], null);

    public static ImportCompensationChangesOutcome InvalidFile(string error) =>
        new(ImportCompensationOutcomeType.InvalidFile, null, [], error);

    public static ImportCompensationChangesOutcome ValidationFailed(IReadOnlyList<CompensationImportRowError> rowErrors) =>
        new(ImportCompensationOutcomeType.ValidationFailed, null, rowErrors, null);
}
