using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeOffboardingPlanCoordinator : IOffboardingPlanCoordinator
{
    public record StartCall(Guid CompanyId, Guid EmployeeId, DateOnly LastWorkingDay, string? Notes, Guid? ReplacementManagerEmployeeId);
    public record CancelOutstandingTasksCall(Guid CompanyId, Guid EmployeeId);
    public record RescheduleOutstandingTasksCall(Guid CompanyId, Guid EmployeeId, DateOnly NewLastWorkingDay);

    public List<StartCall> StartCalls { get; } = [];
    public List<CancelOutstandingTasksCall> CancelOutstandingTasksCalls { get; } = [];
    public List<RescheduleOutstandingTasksCall> RescheduleOutstandingTasksCalls { get; } = [];

    public Task StartAsync(
        Guid companyId,
        Guid employeeId,
        DateOnly lastWorkingDay,
        string? notes,
        Guid? replacementManagerEmployeeId,
        CancellationToken cancellationToken)
    {
        StartCalls.Add(new StartCall(companyId, employeeId, lastWorkingDay, notes, replacementManagerEmployeeId));
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

    public Task RescheduleOutstandingTasksAsync(
        Guid companyId,
        Guid employeeId,
        DateOnly newLastWorkingDay,
        CancellationToken cancellationToken)
    {
        RescheduleOutstandingTasksCalls.Add(new RescheduleOutstandingTasksCall(companyId, employeeId, newLastWorkingDay));
        return Task.CompletedTask;
    }
}
