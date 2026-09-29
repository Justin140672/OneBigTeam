using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Offboarding.Tests.Infrastructure;

internal sealed class FakeTaskReassigner : ITaskReassigner
{
    public record ReassignCall(Guid CompanyId, Guid FromEmployeeId, Guid? ToEmployeeId);

    public List<ReassignCall> Calls { get; } = [];

    public int ReassignReturnCount { get; set; }

    public Task<int> ReassignAllByAssigneeAsync(
        Guid companyId,
        Guid fromEmployeeId,
        Guid? toEmployeeId,
        CancellationToken cancellationToken)
    {
        Calls.Add(new ReassignCall(companyId, fromEmployeeId, toEmployeeId));
        return Task.FromResult(ReassignReturnCount);
    }
}
