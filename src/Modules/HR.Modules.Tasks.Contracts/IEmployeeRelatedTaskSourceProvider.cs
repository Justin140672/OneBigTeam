namespace HR.Modules.Tasks.Contracts;

/// <summary>
/// Implemented by modules that create tasks about an employee which are not necessarily assigned
/// to that employee (e.g. onboarding tasks owned by the manager). The Tasks module includes the
/// tasks whose SourceEntityId is returned here in that employee's consolidated task list.
/// </summary>
public interface IEmployeeRelatedTaskSourceProvider
{
    Task<IReadOnlyCollection<Guid>> GetSourceEntityIdsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken);
}
