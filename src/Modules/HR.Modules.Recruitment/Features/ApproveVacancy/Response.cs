namespace HR.Modules.Recruitment.Features.ApproveVacancy;

internal sealed record ApproveVacancyResponse(
    Guid Id,
    Guid CompanyId,
    DateTimeOffset ApprovedAt,
    Guid ApprovedByUserId);
