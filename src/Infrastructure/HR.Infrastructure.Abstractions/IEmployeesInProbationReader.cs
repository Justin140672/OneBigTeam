namespace HR.Infrastructure.Abstractions;

/// <summary>
/// DSH-05: lets a coordinating query (the manager team-status summary, owned by
/// HR.Modules.Employees) ask "which of these employees are currently serving probation?" without
/// reaching into the Probation module's schema.
///
/// "Currently serving probation" means an <b>active probation record</b> — status Active,
/// ReviewDue or Extended. It deliberately excludes NotStarted (probation start date still in the
/// future), Passed / Failed / NotApplicable (terminal). This is distinct from
/// <see cref="IProbationReviewComplianceReader"/> / upcoming-review readers, which answer "who has
/// a review due soon" — a review being due is not the same as being in probation.
/// </summary>
public interface IEmployeesInProbationReader
{
    /// <summary>Returns the subset of <paramref name="employeeIds"/> with an active probation record.</summary>
    Task<IReadOnlySet<Guid>> GetEmployeeIdsInProbationAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken);
}
