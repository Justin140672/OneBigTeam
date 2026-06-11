namespace HR.Modules.Employees.Features.UpdatePositionProfile;

internal sealed record UpdatePositionProfileRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public Guid? DepartmentId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsManagerial { get; init; }
}
