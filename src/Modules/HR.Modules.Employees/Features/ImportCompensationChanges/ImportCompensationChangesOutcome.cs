namespace HR.Modules.Employees.Features.ImportCompensationChanges;

internal enum ImportCompensationOutcomeType
{
    Success,
    InvalidFile,
    ValidationFailed
}

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
