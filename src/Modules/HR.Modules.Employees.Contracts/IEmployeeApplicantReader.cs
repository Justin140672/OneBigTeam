namespace HR.Modules.Employees.Contracts;

public enum EmployeeApplicantEmploymentState
{
    Draft,
    Active,
    Suspended,
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

public interface IEmployeeApplicantReader
{
    Task<EmployeeApplicantProfile?> GetApplicantAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken);
}
