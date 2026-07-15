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

// Flat, string-keyed shape for Syncfusion's SfDiagramComponent <DataSourceSettings> binding
// (ID/ParentID are matched to these property names by name, not by type, so they must be
// strings — Syncfusion's own organizational-chart sample uses the same shape). Built by
// flattening an already cycle-safe OrganisationChartNode tree back out, rather than binding the
// raw API data directly, so the diagram never has to cope with a manager cycle itself.
public sealed class OrganisationChartDiagramItem
{
    public string Id { get; set; } = string.Empty;
    public string? ManagerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string? ProfilePhotoUrl { get; set; }
}
