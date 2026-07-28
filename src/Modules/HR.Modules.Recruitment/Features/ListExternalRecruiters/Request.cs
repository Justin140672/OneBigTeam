namespace HR.Modules.Recruitment.Features.ListExternalRecruiters;

internal sealed record ListExternalRecruitersRequest(
    Guid CompanyId,
    string? Search,
    bool? IsActive,
    int PageNumber = 1,
    int PageSize = 20);
