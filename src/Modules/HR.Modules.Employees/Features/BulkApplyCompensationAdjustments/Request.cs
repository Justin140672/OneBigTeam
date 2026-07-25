using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.BulkApplyCompensationAdjustments;

internal sealed record BulkCompensationAdjustmentItem
{
    public Guid EmployeeId { get; init; }
    public decimal ProposedSalary { get; init; }
    public SalaryType SalaryType { get; init; }
    public string Currency { get; init; } = string.Empty;
    public decimal? HoursPerWeek { get; init; }
    public decimal? FTE { get; init; }
}

internal sealed record BulkApplyCompensationAdjustmentsRequest
{
    public Guid CompanyId { get; init; }
    public DateOnly EffectiveDate { get; init; }
    public CompensationChangeReason Reason { get; init; }
    public string? Notes { get; init; }
    public CompensationAdjustmentMode AdjustmentMode { get; init; }
    public IReadOnlyList<BulkCompensationAdjustmentItem> Items { get; init; } = [];
}
