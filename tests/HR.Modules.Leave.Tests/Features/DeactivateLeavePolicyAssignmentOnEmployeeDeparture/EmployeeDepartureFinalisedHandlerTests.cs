using HR.Modules.Employees.Contracts;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.DeactivateLeavePolicyAssignmentOnEmployeeDeparture;
using HR.Modules.Leave.Jobs;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests.Features.DeactivateLeavePolicyAssignmentOnEmployeeDeparture;

// Reliability follow-up: this handler no longer deactivates the assignment inline (that work moved
// to LeavePolicyDeactivationJob, exercised by LeavePolicyDeactivationJobTests) — it now only ever
// records a durable LeavePolicyDeactivationOnDeparture request and enqueues the job to process it,
// so a transient failure in the actual deactivation is retryable rather than silently dropped by
// IntegrationEventPublisher's catch-and-log behaviour. See these types' own remarks for the full
// rationale.
public class EmployeeDepartureFinalisedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new LeaveDbContext(options);
    }

    private static EmployeeDepartureFinalisedHandler BuildHandler(
        LeaveDbContext context, RecordingBackgroundJobClient jobClient)
        => new(context, new FakeClock(FixedUtcNow), jobClient);

    [Fact]
    public async Task HandleAsync_Records_Pending_Deactivation_And_Enqueues_Job_For_Active_Assignment()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var assignment = EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), new DateOnly(2026, 1, 1), Now);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        await context.SaveChangesAsync();

        var occurredAt = Now.AddDays(1);
        var jobClient = new RecordingBackgroundJobClient();
        var handler = BuildHandler(context, jobClient);

        await handler.HandleAsync(
            new EmployeeDepartureFinalisedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 6, 9), occurredAt, AccessDisabled: true),
            CancellationToken.None);

        // Assignment itself is untouched by the handler — only LeavePolicyDeactivationJob performs
        // the actual deactivation.
        var savedAssignment = await context.EmployeeLeavePolicyAssignments.SingleAsync();
        Assert.True(savedAssignment.IsActive);

        var request = await context.LeavePolicyDeactivationsOnDeparture.SingleAsync();
        Assert.Equal(companyId, request.CompanyId);
        Assert.Equal(employeeId, request.EmployeeId);
        Assert.Equal(occurredAt, request.OccurredAt);
        Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusPending, request.Status);

        Assert.Single(jobClient.CreatedJobs, j => j.Type == typeof(LeavePolicyDeactivationJob));
    }

    [Fact]
    public async Task HandleAsync_Is_NoOp_When_Assignment_Already_Inactive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var assignment = EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), new DateOnly(2026, 1, 1), Now);
        var firstDeactivatedAt = Now.AddDays(1);
        assignment.Deactivate(firstDeactivatedAt);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        await context.SaveChangesAsync();

        var jobClient = new RecordingBackgroundJobClient();
        var handler = BuildHandler(context, jobClient);
        await handler.HandleAsync(
            new EmployeeDepartureFinalisedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 6, 9), Now.AddDays(5), AccessDisabled: true),
            CancellationToken.None);

        Assert.Empty(context.LeavePolicyDeactivationsOnDeparture);
        Assert.Empty(jobClient.CreatedJobs);
    }

    [Fact]
    public async Task HandleAsync_Is_NoOp_When_No_Assignment_Exists_For_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var jobClient = new RecordingBackgroundJobClient();
        var handler = BuildHandler(context, jobClient);

        // Should not throw and should leave the (empty) tables untouched.
        await handler.HandleAsync(
            new EmployeeDepartureFinalisedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 6, 9), Now, AccessDisabled: true),
            CancellationToken.None);

        Assert.Empty(context.EmployeeLeavePolicyAssignments);
        Assert.Empty(context.LeavePolicyDeactivationsOnDeparture);
        Assert.Empty(jobClient.CreatedJobs);
    }

    [Fact]
    public async Task HandleAsync_Only_Records_Deactivation_For_Matching_Company_And_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();

        var targetAssignment = EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), new DateOnly(2026, 1, 1), Now);
        var otherCompanyAssignment = EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), otherCompanyId, employeeId, Guid.NewGuid(), new DateOnly(2026, 1, 1), Now);
        var otherEmployeeAssignment = EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, otherEmployeeId, Guid.NewGuid(), new DateOnly(2026, 1, 1), Now);

        context.EmployeeLeavePolicyAssignments.AddRange(targetAssignment, otherCompanyAssignment, otherEmployeeAssignment);
        await context.SaveChangesAsync();

        var jobClient = new RecordingBackgroundJobClient();
        var handler = BuildHandler(context, jobClient);
        await handler.HandleAsync(
            new EmployeeDepartureFinalisedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 6, 9), Now, AccessDisabled: true),
            CancellationToken.None);

        var request = await context.LeavePolicyDeactivationsOnDeparture.SingleAsync();
        Assert.Equal(companyId, request.CompanyId);
        Assert.Equal(employeeId, request.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Is_Idempotent_When_Deactivation_Already_Requested()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var assignment = EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), new DateOnly(2026, 1, 1), Now);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        context.LeavePolicyDeactivationsOnDeparture.Add(
            LeavePolicyDeactivationOnDeparture.CreatePending(Guid.NewGuid(), companyId, employeeId, Now, Now));
        await context.SaveChangesAsync();

        var jobClient = new RecordingBackgroundJobClient();
        var handler = BuildHandler(context, jobClient);

        // Simulates redelivery of the same (or a reconciliation-republished) integration event.
        await handler.HandleAsync(
            new EmployeeDepartureFinalisedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 6, 9), Now.AddDays(1), AccessDisabled: true),
            CancellationToken.None);

        Assert.Single(context.LeavePolicyDeactivationsOnDeparture);
        Assert.Empty(jobClient.CreatedJobs);
    }
}
