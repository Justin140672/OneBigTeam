using HR.Modules.Employees.Contracts;
namespace HR.Modules.Recruitment.Features.HireCandidate;

internal sealed record HireCandidateRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }

    // Ticket 2: optional. When omitted, HireCandidateHandler falls back to the proposed start date
    // recorded on the accepted offer (Application.OfferedStartDate) so HR doesn't re-enter it. Hiring
    // fails if neither is present.
    public DateOnly? StartDate { get; init; }

    public DateOnly DateOfBirth { get; init; }
    public string Nationality { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;
    public string? GenderOther { get; init; }
    public string EmployeeNumber { get; init; } = string.Empty;

    // DepartmentId, LocationId, PositionProfileId and EmploymentTypeId are deliberately NOT
    // client-supplied: HireCandidateHandler derives them from the Vacancy (and its Position Profile),
    // so the hired employee cannot be assigned something other than what the vacancy specifies.
    //
    // Manager defaults to Vacancy.HiringManagerId. Set OverrideManager to replace it with ManagerId;
    // OverrideManager with a null ManagerId is an explicit "No manager".
    public bool OverrideManager { get; init; }
    public Guid? ManagerId { get; init; }

    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? County { get; init; }
    public string? PostCode { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
