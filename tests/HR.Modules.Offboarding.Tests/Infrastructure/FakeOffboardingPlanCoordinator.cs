using HR.Infrastructure.Abstractions;

namespace HR.Modules.Offboarding.Tests.Infrastructure;

internal sealed class FakeOffboardingPlanCoordinator : IOffboardingPlanCoordinator
{
    public record StartCall(Guid CompanyId, Guid EmployeeId, DateOnly LastWorkingDay, string? Notes);
    public record CancelOutstandingTasksCall(Guid CompanyId, Guid EmployeeId);

    public List<StartCall> StartCalls { get; } = [];
    public List<CancelOutstandingTasksCall> CancelOutstandingTasksCalls { get; } = [];

    /// <summary>Set to make CancelOutstandingTasksAsync throw for a specific employee — used to
    /// prove one employee's failure doesn't stop a batch (e.g. the reconciliation job) from
    /// processing the rest.</summary>
    public HashSet<Guid> EmployeeIdsThatThrow { get; } = [];

    public Task StartAsync(
        Guid companyId,
        Guid employeeId,
        DateOnly lastWorkingDay,
        string? notes,
        CancellationToken cancellationToken)
    {
        StartCalls.Add(new StartCall(companyId, employeeId, lastWorkingDay, notes));
        return Task.CompletedTask;
    }

    public Task CancelOutstandingTasksAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        CancelOutstandingTasksCalls.Add(new CancelOutstandingTasksCall(companyId, employeeId));

        if (EmployeeIdsThatThrow.Contains(employeeId))
            throw new InvalidOperationException($"Simulated failure for employee {employeeId}.");

        return Task.CompletedTask;
    }
}
