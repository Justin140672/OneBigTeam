namespace HR.Web.Models;

public sealed record OrganisationChartResponse(IReadOnlyList<OrganisationChartEmployeeModel> Items);

public sealed record OrganisationChartEmployeeModel(
    Guid EmployeeId,
    string Name,
    string EmployeeNumber,
    string JobTitle,
    string Department,
    Guid? ManagerId,
    string Location,
    string? ProfilePhotoUrl);

public sealed record OrganisationChartNode(
    Guid EmployeeId,
    string Name,
    string JobTitle,
    string Department,
    string Location,
    string? ProfilePhotoUrl,
    IReadOnlyList<OrganisationChartNode> DirectReports);

public sealed class OrganisationChartDiagramItem
{
    public string Id { get; set; } = string.Empty;
    public string? ManagerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string? ProfilePhotoUrl { get; set; }
}
