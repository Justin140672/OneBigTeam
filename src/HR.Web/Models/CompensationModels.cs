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

// Matches the CompensationChangeReason enum's raw string values — used wherever a Reason needs a
// human-readable label instead of the enum's PascalCase serialized form (e.g. "Compensation
// History" grid). Follows the same convention as EmployeeNoteCategories.Label.
public static class CompensationChangeReasons
{
    public static string Label(string reason) => reason switch
    {
        "NewHire" => "New Hire",
        "AnnualReview" => "Annual Review",
        "Promotion" => "Promotion",
        "MarketAdjustment" => "Market Adjustment",
        "RoleChange" => "Role Change",
        "Correction" => "Correction",
        "Other" => "Other",
        _ => reason
    };
}

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
