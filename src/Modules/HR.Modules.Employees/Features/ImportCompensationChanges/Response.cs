namespace HR.Modules.Employees.Features.ImportCompensationChanges;

internal sealed record ImportedCompensationItem(
    Guid EmployeeId,
    string EmployeeNumber,
    Guid CompensationRecordId,
    decimal NewSalary,
    DateOnly EffectiveDate);

internal sealed record CompensationImportRowError(int RowNumber, string Message);

internal sealed record ImportCompensationChangesResponse(
    Guid ImportBatchId,
    IReadOnlyList<ImportedCompensationItem> Items);
