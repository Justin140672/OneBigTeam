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

    // DepartmentId, LocationId and PositionProfileId are deliberately NOT independent client-supplied
    // inputs here — the whole point of this story is that a hired employee is assigned to the
    // Vacancy's linked Position Profile, not to whatever HR happened to type into this dialog. See
    // HireCandidateHandler: it looks up the Vacancy via VacancyId, takes its (mandatory)
    // PositionProfileId, and derives DepartmentId/LocationId from that same Position Profile via
    // IPositionProfileReader — exactly the same pattern CreateVacancyHandler already uses for
    // DepartmentId. Manager remains a genuinely independent choice HR makes for the new employee.
    public Guid? ManagerId { get; init; }
}
