using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.Employees.Features.ListPositionProfiles;

internal sealed record ListPositionProfilesResponse(IReadOnlyList<PositionProfileListItem> Items);

internal sealed record PositionProfileListItem(
    Guid Id,
    string? DepartmentName,
    string Title,
    bool IsActive,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryType,
    NoticePeriodUnit? NoticePeriodUnitOverride,
    int? NoticePeriodLengthOverride,
    string? LocationName = null);
