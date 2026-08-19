using System.ComponentModel.DataAnnotations;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Web.Models;

// ── LIST ──────────────────────────────────────────────────────────────────────

public record ListPositionProfilesResponse(
    IReadOnlyList<PositionProfileListItemModel> Items);

public record PositionProfileListItemModel(
    Guid Id,
    string? DepartmentName,
    string Title,
    string? Description,
    bool IsActive,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryType,
    NoticePeriodUnit? NoticePeriodUnitOverride = null,
    int? NoticePeriodLengthOverride = null);

// ── GET ───────────────────────────────────────────────────────────────────────

public record GetPositionProfileResponse(
    Guid Id,
    Guid CompanyId,
    Guid DepartmentId,
    Guid LocationId,
    string Title,
    string? Description,
    int? ProbationMonthsOverride,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryType,
    Guid DefaultLeavePolicyId,
    Guid? OnboardingTemplateId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PositionProfileRequiredDocumentModel> RequiredDocuments,
    IReadOnlyList<PositionProfileRequiredAssetModel> RequiredAssets,
    NoticePeriodUnit? NoticePeriodUnitOverride = null,
    int? NoticePeriodLengthOverride = null);

public record PositionProfileRequiredDocumentModel(
    Guid Id,
    Guid DocumentTypeId,
    bool IsMandatory,
    int? DueDaysAfterStart,
    bool RequiresExpiryDate);

public record PositionProfileRequiredAssetModel(
    Guid Id,
    Guid AssetCategoryId,
    bool IsMandatory,
    int Quantity);

// ── EDIT MODEL ────────────────────────────────────────────────────────────────

public sealed class PositionProfileEditModel
{
    [Required(ErrorMessage = "Title is required.")]
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    [Required(ErrorMessage = "Department is required.")]
    public Guid? DepartmentId { get; set; }
    [Required(ErrorMessage = "Location is required.")]
    public Guid? LocationId { get; set; }
    [Range(1, 24, ErrorMessage = "Probation months override must be between 1 and 24.")]
    public int? ProbationMonthsOverride { get; set; }
    public bool UseCompanyWorkingPattern { get; set; } = true;
    public HashSet<string> WorkingWeek { get; set; } = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"];
    [Range(0.5, 24, ErrorMessage = "Hours per day must be between 0.5 and 24.")]
    public decimal HoursPerDay { get; set; } = 7.5m;
    public bool OverrideNoticePeriod { get; set; }
    public NoticePeriodUnit NoticePeriodUnit { get; set; } = NoticePeriodUnit.Weeks;
    [Range(1, int.MaxValue, ErrorMessage = "Notice period length must be greater than 0.")]
    public int NoticePeriodLength { get; set; } = 1;
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public string? SalaryType { get; set; } = "Annual";
    [Required(ErrorMessage = "Default leave policy is required.")]
    public Guid? DefaultLeavePolicyId { get; set; }
    public Guid? OnboardingTemplateId { get; set; }
}

// ── CREATE ────────────────────────────────────────────────────────────────────

public record CreatePositionProfileRequest(
    Guid CompanyId,
    Guid DepartmentId,
    Guid LocationId,
    string Title,
    string? Description,
    int? ProbationMonthsOverride,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryType,
    Guid DefaultLeavePolicyId,
    Guid? OnboardingTemplateId,
    NoticePeriodUnit? NoticePeriodUnitOverride = null,
    int? NoticePeriodLengthOverride = null);

public record CreatePositionProfileResponse(
    Guid Id,
    Guid CompanyId,
    Guid DepartmentId,
    string Title,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt);

// ── LIST REQUIRED DOCUMENTS ───────────────────────────────────────────────────

public record ListRequiredDocumentsResponse(IReadOnlyList<RequiredDocumentListItemModel> Items);

public record RequiredDocumentListItemModel(
    Guid Id,
    Guid DocumentTypeId,
    string DocumentTypeName,
    bool IsMandatory,
    int? DueDaysAfterStart,
    bool RequiresExpiryDate);

// ── ADD / REMOVE REQUIRED DOCUMENT ────────────────────────────────────────────

public record AddRequiredDocumentToProfileRequest(
    Guid CompanyId,
    Guid PositionProfileId,
    Guid DocumentTypeId,
    bool IsMandatory,
    int? DueDaysAfterStart,
    bool RequiresExpiryDate);

public record AddRequiredDocumentToProfileResponse(Guid Id);

// ── LIST REQUIRED ASSETS ──────────────────────────────────────────────────────

public record ListRequiredAssetsResponse(IReadOnlyList<RequiredAssetListItemModel> Items);

public record RequiredAssetListItemModel(
    Guid Id,
    Guid AssetCategoryId,
    string AssetCategoryName,
    bool IsMandatory,
    int Quantity);

// ── ADD / REMOVE REQUIRED ASSET ───────────────────────────────────────────────

public record AddRequiredAssetToProfileRequest(
    Guid CompanyId,
    Guid PositionProfileId,
    Guid AssetCategoryId,
    bool IsMandatory,
    int Quantity);

public record AddRequiredAssetToProfileResponse(Guid Id);

// ── LIST / ADD / REMOVE ONBOARDING TEMPLATES ──────────────────────────────────

public record ListOnboardingTemplatesForProfileResponse(IReadOnlyList<OnboardingTemplateAssignmentListItemModel> Items);

public record OnboardingTemplateAssignmentListItemModel(
    Guid Id,
    Guid OnboardingTemplateId,
    string Name,
    string? Description,
    int TaskCount);

public record AddOnboardingTemplateToProfileRequest(
    Guid CompanyId,
    Guid PositionProfileId,
    Guid OnboardingTemplateId);

public record AddOnboardingTemplateToProfileResponse(Guid Id);

// ── UPDATE ────────────────────────────────────────────────────────────────────

public record UpdatePositionProfileRequest(
    Guid CompanyId,
    Guid Id,
    Guid DepartmentId,
    Guid LocationId,
    string Title,
    string? Description,
    int? ProbationMonthsOverride,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryType,
    Guid DefaultLeavePolicyId,
    Guid? OnboardingTemplateId,
    NoticePeriodUnit? NoticePeriodUnitOverride = null,
    int? NoticePeriodLengthOverride = null);

public record UpdatePositionProfileResponse(
    Guid Id,
    Guid CompanyId,
    Guid DepartmentId,
    string Title,
    string? Description,
    bool IsActive,
    DateTimeOffset UpdatedAt);
