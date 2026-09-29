namespace HR.Modules.Tasks.Contracts;

public interface ITaskReassigner
{
    Task<int> ReassignAllByAssigneeAsync(
        Guid companyId,
        Guid fromEmployeeId,
        Guid? toEmployeeId,
        CancellationToken cancellationToken);
}
