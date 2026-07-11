namespace HR.Modules.Recruitment.Features.HireCandidate;

internal sealed record HireCandidateRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly DateOfBirth { get; init; }
    public string Nationality { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;
    public string? GenderOther { get; init; }
    public string EmployeeNumber { get; init; } = string.Empty;
    public Guid EmploymentTypeId { get; init; }
    public Guid DepartmentId { get; init; }
    public Guid LocationId { get; init; }
    public Guid PositionProfileId { get; init; }
    public Guid? ManagerId { get; init; }
}
