namespace HR.Web.Models;

// ── INTERNAL VACANCIES (employee-facing) ──────────────────────────────────────
// Backed by the read-only "internal-vacancies" endpoints which require only an authenticated
// employee of the company. closingDate / employmentType are currently always null server-side —
// only render them when non-null.

public record InternalVacancyListResponse(List<InternalVacancyListItem> Items);

public record InternalVacancyListItem(
    Guid Id,
    string Title,
    string? DepartmentName,
    string? Location,
    DateOnly? ClosingDate);

public record InternalVacancyDetail(
    Guid Id,
    string Title,
    string? Description,
    string? DepartmentName,
    string? Location,
    string? EmploymentType,
    DateOnly? ClosingDate,
    DateOnly? OpenedAt);
