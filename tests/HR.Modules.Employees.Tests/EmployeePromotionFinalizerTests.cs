using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

// Unit tests for EmployeePromotionFinalizer in isolation, exercised directly rather than through
// PromoteEmployeeHandler or ProcessPromotionsJob that also call it.
public class EmployeePromotionFinalizerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 25, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Employee CreateEmployee(
        Guid companyId, DateTimeOffset now, Guid? managerId = null, Guid? locationId = null, Guid? positionProfileId = null)
    {
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate,
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001",
            Guid.NewGuid(), Guid.NewGuid(), locationId ?? Guid.NewGuid(), positionProfileId ?? Guid.NewGuid(), now);

        if (managerId.HasValue)
            employee.Assign(employee.DepartmentId, employee.PositionProfileId, employee.LocationId, managerId.Value, now);

        return employee;
    }

    private static EmployeePromotion CreatePromotion(
        Guid companyId,
        Guid employeeId,
        Guid previousPositionProfileId,
        Guid newPositionProfileId,
        Guid? newManagerId,
        Guid? newLocationId,
        DateOnly effectiveDate,
        DateTimeOffset now) =>
        EmployeePromotion.Create(
            Guid.NewGuid(), companyId, employeeId, previousPositionProfileId, newPositionProfileId,
            newManagerId, newLocationId, effectiveDate, "Promotion for excellent performance.", notes: null,
            compensationId: null, createdBy: Guid.NewGuid(), now);

    private static EmployeePromotionFinalizer BuildFinalizer(
        EmployeesDbContext dbContext,
        FakeAuditPublisher? auditPublisher = null,
        CapturingIntegrationEventPublisher? integrationEventPublisher = null) =>
        new(
            dbContext,
            auditPublisher ?? new FakeAuditPublisher(),
            integrationEventPublisher ?? new CapturingIntegrationEventPublisher());

    [Fact]
    public async Task FinalizeAsync_Applies_New_Position_To_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var previousPositionProfileId = Guid.NewGuid();
        var newPositionProfileId = Guid.NewGuid();

        var employee = CreateEmployee(companyId, Now, positionProfileId: previousPositionProfileId);
        context.Employees.Add(employee);
        var promotion = CreatePromotion(
            companyId, employee.Id, previousPositionProfileId, newPositionProfileId,
            newManagerId: null, newLocationId: null, DateOnly.FromDateTime(FixedUtcNow), Now);
        context.EmployeePromotions.Add(promotion);
        await context.SaveChangesAsync();

        var finalizer = BuildFinalizer(context);

        await finalizer.FinalizeAsync(employee, promotion, actorEmployeeId: null, Now, CancellationToken.None);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(newPositionProfileId, savedEmployee.PositionProfileId);
    }

    [Fact]
    public async Task FinalizeAsync_Applies_New_Manager_And_Location_When_Specified()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var originalManagerId = Guid.NewGuid();
        var originalLocationId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();
        var newLocationId = Guid.NewGuid();

        var employee = CreateEmployee(companyId, Now, managerId: originalManagerId, locationId: originalLocationId);
        context.Employees.Add(employee);
        var promotion = CreatePromotion(
            companyId, employee.Id, Guid.NewGuid(), Guid.NewGuid(),
            newManagerId, newLocationId, DateOnly.FromDateTime(FixedUtcNow), Now);
        context.EmployeePromotions.Add(promotion);
        await context.SaveChangesAsync();

        var finalizer = BuildFinalizer(context);

        await finalizer.FinalizeAsync(employee, promotion, actorEmployeeId: null, Now, CancellationToken.None);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(newManagerId, savedEmployee.ManagerId);
        Assert.Equal(newLocationId, savedEmployee.LocationId);
    }

    [Fact]
    public async Task FinalizeAsync_Falls_Back_To_Existing_Manager_And_Location_When_Not_Specified()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var originalManagerId = Guid.NewGuid();
        var originalLocationId = Guid.NewGuid();

        var employee = CreateEmployee(companyId, Now, managerId: originalManagerId, locationId: originalLocationId);
        context.Employees.Add(employee);
        var promotion = CreatePromotion(
            companyId, employee.Id, Guid.NewGuid(), Guid.NewGuid(),
            newManagerId: null, newLocationId: null, DateOnly.FromDateTime(FixedUtcNow), Now);
        context.EmployeePromotions.Add(promotion);
        await context.SaveChangesAsync();

        var finalizer = BuildFinalizer(context);

        await finalizer.FinalizeAsync(employee, promotion, actorEmployeeId: null, Now, CancellationToken.None);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(originalManagerId, savedEmployee.ManagerId);
        Assert.Equal(originalLocationId, savedEmployee.LocationId);
    }

    [Fact]
    public async Task FinalizeAsync_Marks_Promotion_Completed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateEmployee(companyId, Now);
        context.Employees.Add(employee);
        var promotion = CreatePromotion(
            companyId, employee.Id, Guid.NewGuid(), Guid.NewGuid(),
            newManagerId: null, newLocationId: null, DateOnly.FromDateTime(FixedUtcNow), Now);
        context.EmployeePromotions.Add(promotion);
        await context.SaveChangesAsync();

        var finalizer = BuildFinalizer(context);

        await finalizer.FinalizeAsync(employee, promotion, actorEmployeeId: null, Now, CancellationToken.None);

        var savedPromotion = await context.EmployeePromotions.SingleAsync();
        Assert.Equal(Now, savedPromotion.CompletedAt);
    }

    [Fact]
    public async Task FinalizeAsync_Publishes_EmployeePromotionCompletedAuditEvent_With_Correct_Values()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var previousPositionProfileId = Guid.NewGuid();
        var newPositionProfileId = Guid.NewGuid();
        var effectiveDate = DateOnly.FromDateTime(FixedUtcNow);

        var employee = CreateEmployee(companyId, Now, positionProfileId: previousPositionProfileId);
        context.Employees.Add(employee);
        var promotion = CreatePromotion(
            companyId, employee.Id, previousPositionProfileId, newPositionProfileId,
            newManagerId: null, newLocationId: null, effectiveDate, Now);
        context.EmployeePromotions.Add(promotion);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var finalizer = BuildFinalizer(context, auditPublisher: auditPublisher);

        await finalizer.FinalizeAsync(employee, promotion, actorEmployeeId: null, Now, CancellationToken.None);

        var auditEvent = Assert.IsType<EmployeePromotionCompletedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(employee.Id, auditEvent.EmployeeId);
        Assert.Equal(promotion.Id, auditEvent.PromotionId);
        Assert.Equal(Now, auditEvent.OccurredAt);
        Assert.Equal(previousPositionProfileId, auditEvent.PreviousPositionProfileId);
        Assert.Equal(newPositionProfileId, auditEvent.NewPositionProfileId);
        Assert.Equal(effectiveDate, auditEvent.EffectiveDate);
    }

    [Fact]
    public async Task FinalizeAsync_Publishes_EmployeePromotedIntegrationEvent_Exactly_Once_With_Correct_Values()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var previousPositionProfileId = Guid.NewGuid();
        var newPositionProfileId = Guid.NewGuid();
        var effectiveDate = DateOnly.FromDateTime(FixedUtcNow);

        var employee = CreateEmployee(companyId, Now, positionProfileId: previousPositionProfileId);
        context.Employees.Add(employee);
        var promotion = CreatePromotion(
            companyId, employee.Id, previousPositionProfileId, newPositionProfileId,
            newManagerId: null, newLocationId: null, effectiveDate, Now);
        context.EmployeePromotions.Add(promotion);
        await context.SaveChangesAsync();

        var integrationEventPublisher = new CapturingIntegrationEventPublisher();
        var finalizer = BuildFinalizer(context, integrationEventPublisher: integrationEventPublisher);

        await finalizer.FinalizeAsync(employee, promotion, actorEmployeeId: null, Now, CancellationToken.None);

        var integrationEvent = Assert.IsType<EmployeePromotedIntegrationEvent>(Assert.Single(integrationEventPublisher.Published));
        Assert.Equal(companyId, integrationEvent.CompanyId);
        Assert.Equal(employee.Id, integrationEvent.EmployeeId);
        Assert.Equal(previousPositionProfileId, integrationEvent.PreviousPositionProfileId);
        Assert.Equal(newPositionProfileId, integrationEvent.NewPositionProfileId);
        Assert.Equal(effectiveDate, integrationEvent.EffectiveDate);
    }

    [Fact]
    public async Task FinalizeAsync_Called_Twice_On_Same_Promotion_Throws_On_Second_Call()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateEmployee(companyId, Now);
        context.Employees.Add(employee);
        var promotion = CreatePromotion(
            companyId, employee.Id, Guid.NewGuid(), Guid.NewGuid(),
            newManagerId: null, newLocationId: null, DateOnly.FromDateTime(FixedUtcNow), Now);
        context.EmployeePromotions.Add(promotion);
        await context.SaveChangesAsync();

        var finalizer = BuildFinalizer(context);

        await finalizer.FinalizeAsync(employee, promotion, actorEmployeeId: null, Now, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => finalizer.FinalizeAsync(employee, promotion, actorEmployeeId: null, Now, CancellationToken.None));
    }
}
