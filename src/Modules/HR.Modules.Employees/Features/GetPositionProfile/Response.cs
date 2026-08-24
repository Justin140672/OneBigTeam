using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.GetPositionProfile;

internal sealed record GetPositionProfileResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    Guid? LocationId,
    string Title,
    string? Description,
    int? ProbationMonthsOverride,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    NoticePeriodUnit? NoticePeriodUnitOverride,
    int? NoticePeriodLengthOverride,
    decimal? SalaryMin,
    decimal? SalaryMax,
    SalaryType? SalaryType,
    Guid? DefaultLeavePolicyId,
    Guid? OnboardingTemplateId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<RequiredDocumentItem> RequiredDocuments,
    IReadOnlyList<RequiredAssetItem> RequiredAssets,
    IReadOnlyList<AssignedEmployeeItem> AssignedEmployees);

internal sealed record AssignedEmployeeItem(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    EmploymentStatus Status);

internal sealed record RequiredDocumentItem(
    Guid Id,
    Guid DocumentTypeId,
    bool IsMandatory,
    int? DueDaysAfterStart,
    bool RequiresExpiryDate);

internal sealed record RequiredAssetItem(
    Guid Id,
    Guid AssetCategoryId,
    bool IsMandatory,
    int Quantity);
