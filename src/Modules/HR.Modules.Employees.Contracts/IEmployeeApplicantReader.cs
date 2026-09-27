namespace HR.Modules.Employees.Contracts;

/// <summary>
/// An employee's employment state, as seen by a consumer deciding whether the employee may take part
/// in an employee-only workflow. Mirrors HR.Modules.Employees' own EmploymentStatus without exposing it.
/// </summary>
public enum EmployeeApplicantEmploymentState
{
    /// <summary>The employee record exists but is incomplete / not yet started.</summary>
    Draft,
    Active,
    Suspended,
    /// <summary>A leaving process has started.</summary>
    Leaving,
    Former,
}

/// <summary>
/// The authoritative identity of an employee applying for an internally advertised vacancy (internal
/// recruitment Ticket 4). Recruitment copies these values onto the employee-linked Candidate record.
/// </summary>
public sealed record EmployeeApplicantProfile(
    Guid EmployeeId,
    Guid CompanyId,
    string FirstName,
    string LastName,
    string WorkEmail,
    string? PhoneNumber,
    EmployeeApplicantEmploymentState EmploymentState);

/// <summary>
/// Cross-module read port (implemented in HR.Modules.Employees, consumed by HR.Modules.Recruitment's
/// internal-vacancy Apply slice). Always company-scoped: an employee belonging to a different company
/// is indistinguishable from one that does not exist.
/// </summary>
public interface IEmployeeApplicantReader
{
    /// <summary>Returns null when no employee with this id exists in the given company.</summary>
    Task<EmployeeApplicantProfile?> GetApplicantAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken);
}
