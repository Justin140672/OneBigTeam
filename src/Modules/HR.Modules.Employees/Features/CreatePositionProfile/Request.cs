namespace HR.Modules.Employees.Features.CreatePositionProfile;

internal sealed record CreatePositionProfileRequest
{
    public Guid CompanyId { get; init; }
    public Guid? DepartmentId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsManagerial { get; init; }
}
