using HR.Modules.Employees.Contracts;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.DeactivateLeavePolicyAssignmentOnEmployeeDeparture;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests.Features.DeactivateLeavePolicyAssignmentOnEmployeeDeparture;

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

    [Fact]
    public async Task HandleAsync_Deactivates_Existing_Active_Assignment()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var assignment = EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), new DateOnly(2026, 1, 1), Now);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        await context.SaveChangesAsync();

        var occurredAt = Now.AddDays(1);
        var handler = new EmployeeDepartureFinalisedHandler(context);

        await handler.HandleAsync(
            new EmployeeDepartureFinalisedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 6, 9), occurredAt),
            CancellationToken.None);

        var saved = await context.EmployeeLeavePolicyAssignments.SingleAsync();
        Assert.False(saved.IsActive);
        Assert.Equal(occurredAt, saved.DeactivatedAt);
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

        // Simulate re-delivery of the integration event (or an amended/re-finalised departure)
        // with a later OccurredAt — the original DeactivatedAt must be preserved.
        var handler = new EmployeeDepartureFinalisedHandler(context);
        await handler.HandleAsync(
            new EmployeeDepartureFinalisedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 6, 9), Now.AddDays(5)),
            CancellationToken.None);

        var saved = await context.EmployeeLeavePolicyAssignments.SingleAsync();
        Assert.False(saved.IsActive);
        Assert.Equal(firstDeactivatedAt, saved.DeactivatedAt);
    }

    [Fact]
    public async Task HandleAsync_Is_NoOp_When_No_Assignment_Exists_For_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var handler = new EmployeeDepartureFinalisedHandler(context);

        // Should not throw and should leave the (empty) table untouched.
        await handler.HandleAsync(
            new EmployeeDepartureFinalisedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 6, 9), Now),
            CancellationToken.None);

        Assert.Empty(context.EmployeeLeavePolicyAssignments);
    }

    [Fact]
    public async Task HandleAsync_Only_Deactivates_Assignment_For_Matching_Company_And_Employee()
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

        var handler = new EmployeeDepartureFinalisedHandler(context);
        await handler.HandleAsync(
            new EmployeeDepartureFinalisedIntegrationEvent(companyId, employeeId, new DateOnly(2026, 6, 9), Now),
            CancellationToken.None);

        var saved = await context.EmployeeLeavePolicyAssignments.ToListAsync();
        Assert.False(saved.Single(a => a.Id == targetAssignment.Id).IsActive);
        Assert.True(saved.Single(a => a.Id == otherCompanyAssignment.Id).IsActive);
        Assert.True(saved.Single(a => a.Id == otherEmployeeAssignment.Id).IsActive);
    }
}
