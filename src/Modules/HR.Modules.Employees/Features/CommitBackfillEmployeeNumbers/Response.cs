namespace HR.Modules.Employees.Features.CommitBackfillEmployeeNumbers;

internal sealed record BackfillResultItem(Guid EmployeeId, string AssignedEmployeeNumber);

internal sealed record CommitBackfillEmployeeNumbersResponse(
    Guid BackfillOperationId,
    IReadOnlyList<BackfillResultItem> Items,
    int TotalCount);
