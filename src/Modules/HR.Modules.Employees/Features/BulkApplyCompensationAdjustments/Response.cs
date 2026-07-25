namespace HR.Modules.Employees.Features.BulkApplyCompensationAdjustments;

internal sealed record BulkCompensationAdjustmentResultItem(
    Guid EmployeeId,
    Guid CompensationRecordId,
    decimal PreviousSalary,
    decimal NewSalary,
    DateOnly EffectiveFrom);

internal sealed record BulkApplyCompensationAdjustmentsResponse(
    Guid BulkOperationId,
    IReadOnlyList<BulkCompensationAdjustmentResultItem> Items);
