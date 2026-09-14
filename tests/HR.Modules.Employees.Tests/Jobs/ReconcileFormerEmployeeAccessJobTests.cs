using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Jobs;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Employees.Tests.Jobs;

// P1 fix (departure access disablement): unit tests for the one-off backfill/reconciliation sweep
// that republishes EmployeeDepartureFinalisedIntegrationEvent for former employees whose access was
// already recorded as disabled (HasSystemAccess=false) before Identity started consuming that event
// to actually disable the linked ApplicationUser.
public class ReconcileFormerEmployeeAccessJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 11, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Employee CreateEmployee(
        Guid companyId,
        bool hasSystemAccess,
        EmploymentStatus status,
        DateTimeOffset? reconciledAt = null)
    {
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate,
            hasSystemAccess, new DateOnly(1990, 1, 1), "British", "Prefer not to say",
            $"EMP-{Guid.NewGuid():N}"[..12], Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        switch (status)
        {
            case EmploymentStatus.Active:
                employee.Activate(Now);
                break;
            case EmploymentStatus.Leaving:
                employee.SetLeaving(Now);
                break;
            case EmploymentStatus.FormerEmployee:
                employee.SetFormerEmployee(Now);
                break;
        }

        if (reconciledAt.HasValue)
            employee.MarkAccessDisablementReconciled(reconciledAt.Value);

        return employee;
    }

    private static ReconcileFormerEmployeeAccessJob BuildJob(
        EmployeesDbContext context, CapturingIntegrationEventPublisher publisher) =>
        new(context, publisher, new FakeClock(FixedUtcNow), NullLogger<ReconcileFormerEmployeeAccessJob>.Instance);

    [Fact]
    public async Task ExecuteAsync_Republishes_And_Marks_Reconciled_For_Eligible_FormerEmployee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, hasSystemAccess: false, status: EmploymentStatus.FormerEmployee);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var publisher = new CapturingIntegrationEventPublisher();
        var job = BuildJob(context, publisher);

        await job.ExecuteAsync();

        var integrationEvent = Assert.Single(
            publisher.Published.OfType<EmployeeDepartureFinalisedIntegrationEvent>());
        Assert.Equal(companyId, integrationEvent.CompanyId);
        Assert.Equal(employee.Id, integrationEvent.EmployeeId);
        Assert.True(integrationEvent.AccessDisabled);

        var reloaded = await context.Employees.SingleAsync(e => e.Id == employee.Id);
        Assert.NotNull(reloaded.AccessDisablementReconciledAt);
        Assert.Equal(Now, reloaded.AccessDisablementReconciledAt);
    }

    [Fact]
    public async Task ExecuteAsync_Skips_Employee_Already_Reconciled()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var reconciledAt = Now.AddDays(-1);
        var employee = CreateEmployee(
            companyId, hasSystemAccess: false, status: EmploymentStatus.FormerEmployee, reconciledAt: reconciledAt);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var publisher = new CapturingIntegrationEventPublisher();
        var job = BuildJob(context, publisher);

        await job.ExecuteAsync();

        Assert.Empty(publisher.Published);
        var reloaded = await context.Employees.SingleAsync(e => e.Id == employee.Id);
        Assert.Equal(reconciledAt, reloaded.AccessDisablementReconciledAt);
    }

    [Fact]
    public async Task ExecuteAsync_Skips_Employee_With_HasSystemAccess_True()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, hasSystemAccess: true, status: EmploymentStatus.FormerEmployee);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var publisher = new CapturingIntegrationEventPublisher();
        var job = BuildJob(context, publisher);

        await job.ExecuteAsync();

        Assert.Empty(publisher.Published);
        var reloaded = await context.Employees.SingleAsync(e => e.Id == employee.Id);
        Assert.Null(reloaded.AccessDisablementReconciledAt);
    }

    [Fact]
    public async Task ExecuteAsync_Skips_Employee_Still_Active()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, hasSystemAccess: false, status: EmploymentStatus.Active);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var publisher = new CapturingIntegrationEventPublisher();
        var job = BuildJob(context, publisher);

        await job.ExecuteAsync();

        Assert.Empty(publisher.Published);
        var reloaded = await context.Employees.SingleAsync(e => e.Id == employee.Id);
        Assert.Null(reloaded.AccessDisablementReconciledAt);
    }

    [Fact]
    public async Task ExecuteAsync_Skips_Employee_Still_Leaving_Not_Yet_FormerEmployee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, hasSystemAccess: false, status: EmploymentStatus.Leaving);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var publisher = new CapturingIntegrationEventPublisher();
        var job = BuildJob(context, publisher);

        await job.ExecuteAsync();

        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task ExecuteAsync_Processes_All_Eligible_Employees_Across_Companies_Exactly_Once_Each()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        var eligibleA1 = CreateEmployee(companyA, hasSystemAccess: false, status: EmploymentStatus.FormerEmployee);
        var eligibleA2 = CreateEmployee(companyA, hasSystemAccess: false, status: EmploymentStatus.FormerEmployee);
        var eligibleB1 = CreateEmployee(companyB, hasSystemAccess: false, status: EmploymentStatus.FormerEmployee);
        var alreadyReconciled = CreateEmployee(
            companyB, hasSystemAccess: false, status: EmploymentStatus.FormerEmployee, reconciledAt: Now.AddDays(-2));
        var stillActive = CreateEmployee(companyA, hasSystemAccess: false, status: EmploymentStatus.Active);
        var neverDisabled = CreateEmployee(companyB, hasSystemAccess: true, status: EmploymentStatus.FormerEmployee);

        context.Employees.AddRange(eligibleA1, eligibleA2, eligibleB1, alreadyReconciled, stillActive, neverDisabled);
        await context.SaveChangesAsync();

        var publisher = new CapturingIntegrationEventPublisher();
        var job = BuildJob(context, publisher);

        await job.ExecuteAsync();

        var publishedEmployeeIds = publisher.Published
            .OfType<EmployeeDepartureFinalisedIntegrationEvent>()
            .Select(e => e.EmployeeId)
            .ToList();

        Assert.Equal(3, publishedEmployeeIds.Count);
        Assert.Contains(eligibleA1.Id, publishedEmployeeIds);
        Assert.Contains(eligibleA2.Id, publishedEmployeeIds);
        Assert.Contains(eligibleB1.Id, publishedEmployeeIds);

        foreach (var id in new[] { eligibleA1.Id, eligibleA2.Id, eligibleB1.Id })
        {
            var reloaded = await context.Employees.SingleAsync(e => e.Id == id);
            Assert.NotNull(reloaded.AccessDisablementReconciledAt);
        }
    }
}
