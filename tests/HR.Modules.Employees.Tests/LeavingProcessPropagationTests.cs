using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Employees.Tests;

public class LeavingProcessPropagationTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private sealed class ScriptedPublisher(Func<IIntegrationEvent, bool> outcome) : IIntegrationEventPublisher
    {
        public List<IIntegrationEvent> Delivered { get; } = [];

        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent => Task.CompletedTask;

        public Task<bool> PublishAndConfirmAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent
        {
            Delivered.Add(integrationEvent);
            return Task.FromResult(outcome(integrationEvent));
        }
    }

    private static EmployeesDbContext NewContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static LeavingProcessPropagationService NewService(EmployeesDbContext db, IIntegrationEventPublisher publisher) =>
        new(db, publisher, new FakeClock(FixedUtcNow.AddMinutes(1)), NullLogger<LeavingProcessPropagationService>.Instance);

    private static EmployeeLeavingProcess NewProcess(Guid companyId, Guid employeeId) =>
        EmployeeLeavingProcess.Create(
            Guid.NewGuid(), companyId, employeeId,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 30),
            NoticePeriodUnit.Weeks, 4, NoticePeriodSource.Employee, LeavingReason.Resignation,
            Guid.NewGuid(), Now);

    [Fact]
    public void MarkAttemptFailed_Backs_Off_Then_Becomes_Terminal_At_MaxAttempts()
    {
        var p = LeavingProcessPropagation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeavingProcessPropagation.OperationStarted,
            new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 30), Now, Now, null, null);

        p.MarkAttemptFailed("boom", Now);
        Assert.Equal(LeavingProcessPropagation.StatusPending, p.Status);
        Assert.Equal(1, p.AttemptCount);
        Assert.Equal(Now.AddSeconds(2), p.NextAttemptAt);
        Assert.Equal("boom", p.LastError);

        for (var i = 1; i < LeavingProcessPropagation.MaxAttempts; i++)
            p.MarkAttemptFailed("boom", Now);

        Assert.Equal(LeavingProcessPropagation.StatusFailed, p.Status);
        Assert.Null(p.NextAttemptAt);

        p.ResetForRetry(Now);
        Assert.Equal(LeavingProcessPropagation.StatusPending, p.Status);
        Assert.Equal(0, p.AttemptCount);
    }

    [Fact]
    public async Task Failed_Delivery_Stays_Pending_And_Later_Messages_For_Same_Employee_Wait_For_It()
    {
        await using var db = NewContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var allow = false;
        var publisher = new ScriptedPublisher(_ => allow);
        var service = NewService(db, publisher);

        var process = NewProcess(companyId, employeeId);
        var first = service.Stage(process, LeavingProcessPropagation.OperationStarted, Now);
        var second = service.Stage(process, LeavingProcessPropagation.OperationCancelled, Now.AddSeconds(1));
        service.Stage(NewProcess(companyId, otherEmployeeId), LeavingProcessPropagation.OperationStarted, Now.AddSeconds(2));
        await db.SaveChangesAsync();

        await service.DispatchDueAsync(10, CancellationToken.None);

        Assert.Equal(LeavingProcessPropagation.StatusPending, first.Status);
        Assert.Equal(1, first.AttemptCount);
        Assert.Equal(LeavingProcessPropagation.StatusPending, second.Status);
        Assert.Equal(0, second.AttemptCount);
        Assert.Equal(2, publisher.Delivered.Count);

        allow = true;
        first.ResetForRetry(Now);
        await db.SaveChangesAsync();
        await service.DispatchDueAsync(10, CancellationToken.None);
        await service.DispatchDueAsync(10, CancellationToken.None);

        Assert.Equal(LeavingProcessPropagation.StatusProcessed, first.Status);
        Assert.Equal(LeavingProcessPropagation.StatusProcessed, second.Status);
    }

    [Fact]
    public async Task EnqueueMissing_Only_Targets_Latest_Process_Per_Employee_Without_Rows_And_Is_Idempotent()
    {
        await using var db = NewContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var service = NewService(db, new ScriptedPublisher(_ => true));

        var older = NewProcess(companyId, employeeId);
        older.Cancel("changed mind", Now.AddDays(1));
        var newer = EmployeeLeavingProcess.Create(
            Guid.NewGuid(), companyId, employeeId,
            new DateOnly(2026, 8, 1), new DateOnly(2026, 11, 1), new DateOnly(2026, 10, 31),
            NoticePeriodUnit.Weeks, 4, NoticePeriodSource.Employee, LeavingReason.Resignation,
            Guid.NewGuid(), Now.AddDays(2));
        db.EmployeeLeavingProcesses.AddRange(older, newer);
        await db.SaveChangesAsync();

        Assert.Equal(1, await service.EnqueueMissingAsync(10, CancellationToken.None));
        Assert.Equal(0, await service.EnqueueMissingAsync(10, CancellationToken.None));

        var row = await db.LeavingProcessPropagations.SingleAsync();
        Assert.Equal(newer.Id, row.LeavingProcessId);
        Assert.Equal(LeavingProcessPropagation.OperationReconciledStarted, row.OperationType);
    }
}
