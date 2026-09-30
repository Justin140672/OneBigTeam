namespace HR.Web.Models;


public record InternalVacancyListResponse(List<InternalVacancyListItem> Items);

public record InternalVacancyListItem(
    Guid Id,
    string Title,
    string? DepartmentName,
    string? Location,
    DateOnly? ClosingDate,
    bool HasApplied = false,
    decimal? SalaryMin = null,
    decimal? SalaryMax = null,
    string? SalaryType = null);

public record InternalVacancyDetail(
    Guid Id,
    string Title,
    string? Description,
    string? DepartmentName,
    string? Location,
    string? EmploymentType,
    DateOnly? ClosingDate,
    DateOnly? OpenedAt,
    decimal? SalaryMin = null,
    decimal? SalaryMax = null,
    string? SalaryType = null);

public enum InternalVacancyApplyOutcome
{
    Submitted,
    AlreadyApplied,
    NotEligible,
    EmailInUse,
    VacancyUnavailable,
    Failed
}

public sealed record InternalVacancyApplyResult(InternalVacancyApplyOutcome Outcome, string? Message);
