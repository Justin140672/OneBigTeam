using HR.Modules.Employees.Contracts;
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

    // Optional — captured on the Hire Candidate dialog and forwarded onto the new employee's
    // contact details via IEmployeeProvisioningService/CreateEmployeeHandler (same address fields
    // already used on Employee Edit). All optional since a candidate's address is not guaranteed
    // to be known at hire time.
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? County { get; init; }
    public string? PostCode { get; init; }
}
