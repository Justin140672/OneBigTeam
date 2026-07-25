using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeOffboardingPlanCoordinator : IOffboardingPlanCoordinator
{
    public record StartCall(Guid CompanyId, Guid EmployeeId, DateOnly LastWorkingDay, string? Notes);
    public record CancelOutstandingTasksCall(Guid CompanyId, Guid EmployeeId);

    public List<StartCall> StartCalls { get; } = [];
    public List<CancelOutstandingTasksCall> CancelOutstandingTasksCalls { get; } = [];

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
        return Task.CompletedTask;
    }
}
