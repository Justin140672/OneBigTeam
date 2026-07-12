namespace HR.Modules.Employees.Features.GetOrganisationChart;

internal sealed record GetOrganisationChartResponse(IReadOnlyList<OrganisationChartEmployeeItem> Items);

internal sealed record OrganisationChartEmployeeItem(
    Guid EmployeeId,
    string Name,
    string JobTitle,
    string Department,
    Guid? ManagerId,
    string Location,
    string? ProfilePhotoUrl);
