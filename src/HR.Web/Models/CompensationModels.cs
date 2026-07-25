namespace HR.Web.Models;

public sealed record CurrentCompensationModel(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string SalaryType,
    decimal Salary,
    decimal? AnnualisedSalary,
    string Currency,
    decimal? HoursPerWeek,
    decimal? FTE,
    string? Notes,
    string Reason,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CompensationHistoryItemModel(
    Guid Id,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string SalaryType,
    decimal Salary,
    string Currency,
    decimal? HoursPerWeek,
    decimal? FTE,
    string? Notes,
    string Reason,
    Guid CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record GetCompensationHistoryResponse(IReadOnlyList<CompensationHistoryItemModel> Items);

// ── CREATE ────────────────────────────────────────────────────────────────────

public sealed record CreateCompensationRecordRequest(
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly EffectiveFrom,
    string SalaryType,
    decimal Salary,
    string Currency,
    decimal? HoursPerWeek,
    decimal? FTE,
    string? Notes,
    string Reason);

public sealed record CreateCompensationRecordResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string SalaryType,
    decimal Salary,
    string Currency,
    decimal? HoursPerWeek,
    decimal? FTE,
    string? Notes,
    string Reason,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── UPDATE (future-dated only) ─────────────────────────────────────────────────

public sealed record UpdateFutureCompensationRecordRequest(
    Guid CompanyId,
    Guid EmployeeId,
    Guid Id,
    string SalaryType,
    decimal Salary,
    string Currency,
    decimal? HoursPerWeek,
    decimal? FTE,
    string? Notes,
    string Reason);

public sealed record UpdateFutureCompensationRecordResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string SalaryType,
    decimal Salary,
    string Currency,
    decimal? HoursPerWeek,
    decimal? FTE,
    string? Notes,
    string Reason,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── BULK ADJUSTMENT ─────────────────────────────────────────────────────────

public sealed record BulkCompensationAdjustmentItem(
    Guid EmployeeId,
    decimal ProposedSalary,
    string SalaryType,
    string Currency,
    decimal? HoursPerWeek,
    decimal? FTE);

public sealed record BulkApplyCompensationAdjustmentsRequest(
    Guid CompanyId,
    DateOnly EffectiveDate,
    string Reason,
    string? Notes,
    string AdjustmentMode,
    IReadOnlyList<BulkCompensationAdjustmentItem> Items);

public sealed record BulkCompensationAdjustmentResultItem(
    Guid EmployeeId,
    Guid CompensationRecordId,
    decimal PreviousSalary,
    decimal NewSalary,
    DateOnly EffectiveFrom);

public sealed record BulkApplyCompensationAdjustmentsResponse(
    Guid BulkOperationId,
    IReadOnlyList<BulkCompensationAdjustmentResultItem> Items);

// ── IMPORT ───────────────────────────────────────────────────────────────────

public sealed record ImportedCompensationItem(
    Guid EmployeeId,
    string EmployeeNumber,
    Guid CompensationRecordId,
    decimal NewSalary,
    DateOnly EffectiveDate);

public sealed record CompensationImportRowError(int RowNumber, string Message);

public sealed record ImportCompensationChangesResponse(
    Guid ImportBatchId,
    IReadOnlyList<ImportedCompensationItem> Items);
