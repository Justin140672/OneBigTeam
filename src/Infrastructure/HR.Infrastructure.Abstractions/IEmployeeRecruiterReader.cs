namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Resolves the recruiter (or, when no external recruiter is assigned, the hiring manager) who
/// was responsible for hiring each employee, as owned by HR.Modules.Recruitment. Used by
/// HR.Modules.Employees (composing the Employee Starter Report row) without a direct
/// module-to-module reference. Employees who were not hired through the recruitment pipeline
/// (imported/manually created) are simply absent from the returned dictionary.
/// </summary>
public interface IEmployeeRecruiterReader
{
    Task<IReadOnlyDictionary<Guid, string>> GetRecruiterNamesAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken);
}
