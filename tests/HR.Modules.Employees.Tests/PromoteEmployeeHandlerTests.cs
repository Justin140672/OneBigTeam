using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.PromoteEmployee;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class PromoteEmployeeHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);
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

    private static PromoteEmployeeRequest BuildRequest(
        Guid companyId,
        Guid employeeId,
        Guid newPositionProfileId,
        DateOnly? effectiveDate = null,
        bool confirmBackdatedEffectiveDate = false,
        Guid? newManagerId = null,
        Guid? newLocationId = null,
        bool createCompensationChange = false,
        SalaryType? compensationSalaryType = null,
        decimal? compensationSalary = null,
        string? compensationCurrency = null) =>
        new(
            companyId,
            employeeId,
            newPositionProfileId,
            effectiveDate ?? Today,
            "Promotion for excellent performance.",
            Notes: null,
            newManagerId,
            newLocationId,
            confirmBackdatedEffectiveDate,
            createCompensationChange,
            compensationSalaryType,
            compensationSalary,
            compensationCurrency);

    private static PromoteEmployeeHandler BuildHandler(
        EmployeesDbContext context,
        FakeAuditPublisher? auditPublisher = null,
        CapturingIntegrationEventPublisher? integrationEventPublisher = null,
        DateTime? fixedUtcNow = null,
        FakeCompanyTimeZoneReader? companyTimeZoneReader = null)
    {
        auditPublisher ??= new FakeAuditPublisher();
        integrationEventPublisher ??= new CapturingIntegrationEventPublisher();

        var finalizer = new EmployeePromotionFinalizer(context, auditPublisher, integrationEventPublisher);

        return new(
            context,
            new FakeClock(fixedUtcNow ?? FixedUtcNow),
            companyTimeZoneReader ?? new FakeCompanyTimeZoneReader(),
            new CompensationRecordWriter(context, new FakeClock(fixedUtcNow ?? FixedUtcNow)),
            auditPublisher,
            finalizer);
    }

    [Fact]
    public async Task HandleAsync_Creates_Promotion_And_Applies_Immediately_When_EffectiveDate_Is_Today()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var newPositionProfileId = Guid.NewGuid();
        var handler = BuildHandler(context);
        var actorEmployeeId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id, newPositionProfileId), actorEmployeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newPositionProfileId, result.Value!.NewPositionProfileId);
        Assert.NotNull(result.Value.CompletedAt);

        var savedPromotion = await context.EmployeePromotions.SingleAsync();
        Assert.Equal(newPositionProfileId, savedPromotion.NewPositionProfileId);
        Assert.NotNull(savedPromotion.CompletedAt);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(newPositionProfileId, savedEmployee.PositionProfileId);
    }

    [Fact]
    public async Task HandleAsync_Captures_PreviousPositionProfileId_Before_Applying_Change()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var previousPositionProfileId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, Now, positionProfileId: previousPositionProfileId);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id, Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(previousPositionProfileId, result.Value!.PreviousPositionProfileId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var employee = CreateEmployee(Guid.NewGuid(), Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), employee.Id, Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_EffectiveDate_Is_Backdated_And_Not_Confirmed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, auditPublisher: auditPublisher);

        var result = await handler.HandleAsync(
            BuildRequest(
                companyId, employee.Id, Guid.NewGuid(),
                effectiveDate: Today.AddDays(-1),
                confirmBackdatedEffectiveDate: false),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
        Assert.Equal(0, await context.EmployeePromotions.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_Applies_Immediately_When_EffectiveDate_Is_Backdated_And_Confirmed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var newPositionProfileId = Guid.NewGuid();
        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(
                companyId, employee.Id, newPositionProfileId,
                effectiveDate: Today.AddDays(-1),
                confirmBackdatedEffectiveDate: true),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.CompletedAt);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(newPositionProfileId, savedEmployee.PositionProfileId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Apply_Immediately_When_EffectiveDate_Is_In_The_Future()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var originalPositionProfileId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, Now, positionProfileId: originalPositionProfileId);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var newPositionProfileId = Guid.NewGuid();
        var integrationEventPublisher = new CapturingIntegrationEventPublisher();
        var handler = BuildHandler(context, integrationEventPublisher: integrationEventPublisher);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id, newPositionProfileId, effectiveDate: Today.AddDays(1)),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.CompletedAt);

        var savedPromotion = await context.EmployeePromotions.SingleAsync();
        Assert.Null(savedPromotion.CompletedAt);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(originalPositionProfileId, savedEmployee.PositionProfileId);

        Assert.Empty(integrationEventPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Fails_Whole_Submission_When_CompensationWrite_Fails_And_Persists_Nothing()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, Now);
        context.Employees.Add(employee);

        // Seed an existing overlapping compensation record so CompensationRecordWriter.WriteAsync fails.
        var existingCompensation = Compensation.Create(
            Guid.NewGuid(), companyId, employee.Id, Today, SalaryType.Annual, 50000m, "GBP",
            hoursPerWeek: null, fte: null, notes: null, CompensationChangeReason.NewHire, Guid.NewGuid(), Now);
        context.Compensations.Add(existingCompensation);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, auditPublisher: auditPublisher);

        var result = await handler.HandleAsync(
            BuildRequest(
                companyId, employee.Id, Guid.NewGuid(),
                createCompensationChange: true,
                compensationSalaryType: SalaryType.Annual,
                compensationSalary: 60000m,
                compensationCurrency: "GBP"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);

        Assert.Equal(0, await context.EmployeePromotions.CountAsync());
        Assert.Equal(1, await context.Compensations.CountAsync());
        Assert.Empty(auditPublisher.Published);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(employee.PositionProfileId, savedEmployee.PositionProfileId);
    }

    [Fact]
    public async Task HandleAsync_Writes_Compensation_Record_Before_Creating_Promotion_When_Requested()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(
                companyId, employee.Id, Guid.NewGuid(),
                createCompensationChange: true,
                compensationSalaryType: SalaryType.Annual,
                compensationSalary: 65000m,
                compensationCurrency: "GBP"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.CompensationId);

        var savedCompensation = await context.Compensations.SingleAsync();
        Assert.Equal(result.Value.CompensationId, savedCompensation.Id);
        Assert.Equal(65000m, savedCompensation.Salary);

        var savedPromotion = await context.EmployeePromotions.SingleAsync();
        Assert.Equal(savedCompensation.Id, savedPromotion.CompensationId);
    }

    [Fact]
    public async Task HandleAsync_Publishes_EmployeePromotionRequestedAuditEvent_With_Real_Actor()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, auditPublisher: auditPublisher);
        var actorEmployeeId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id, Guid.NewGuid(), effectiveDate: Today.AddDays(1)),
            actorEmployeeId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var requested = Assert.IsType<EmployeePromotionRequestedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(companyId, requested.CompanyId);
        Assert.Equal(employee.Id, requested.EmployeeId);
        Assert.Equal(actorEmployeeId, requested.ActorEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Publishes_EmployeePromotionCompletedAuditEvent_With_System_Attribution_When_Applied_Immediately()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, auditPublisher: auditPublisher);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id, Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, auditPublisher.Published.Count);
        Assert.Contains(auditPublisher.Published, e => e is EmployeePromotionRequestedAuditEvent);

        var completed = Assert.IsType<EmployeePromotionCompletedAuditEvent>(
            auditPublisher.Published.Single(e => e is EmployeePromotionCompletedAuditEvent));
        Assert.Null(((IAuditEvent)completed).ActorUserId);
        Assert.Null(((IAuditEvent)completed).ActorEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Publishes_IntegrationEvent_Exactly_Once_When_Applied_Immediately()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var integrationEventPublisher = new CapturingIntegrationEventPublisher();
        var handler = BuildHandler(context, integrationEventPublisher: integrationEventPublisher);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id, Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(integrationEventPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Publishes_No_IntegrationEvent_When_Deferred()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var integrationEventPublisher = new CapturingIntegrationEventPublisher();
        var handler = BuildHandler(context, integrationEventPublisher: integrationEventPublisher);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id, Guid.NewGuid(), effectiveDate: Today.AddDays(1)),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(integrationEventPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Leaves_Manager_And_Location_Unchanged_When_Not_Specified()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var originalManagerId = Guid.NewGuid();
        var originalLocationId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, Now, managerId: originalManagerId, locationId: originalLocationId);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id, Guid.NewGuid(), newManagerId: null, newLocationId: null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(originalManagerId, savedEmployee.ManagerId);
        Assert.Equal(originalLocationId, savedEmployee.LocationId);
    }

    [Fact]
    public async Task HandleAsync_Changes_Manager_Only_When_Only_NewManagerId_Specified()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var originalLocationId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, Now, managerId: Guid.NewGuid(), locationId: originalLocationId);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var newManagerId = Guid.NewGuid();
        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id, Guid.NewGuid(), newManagerId: newManagerId, newLocationId: null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(newManagerId, savedEmployee.ManagerId);
        Assert.Equal(originalLocationId, savedEmployee.LocationId);
    }

    [Fact]
    public async Task HandleAsync_Changes_Location_Only_When_Only_NewLocationId_Specified()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var originalManagerId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, Now, managerId: originalManagerId, locationId: Guid.NewGuid());
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var newLocationId = Guid.NewGuid();
        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id, Guid.NewGuid(), newManagerId: null, newLocationId: newLocationId),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(originalManagerId, savedEmployee.ManagerId);
        Assert.Equal(newLocationId, savedEmployee.LocationId);
    }

    [Fact]
    public async Task HandleAsync_Changes_Both_Manager_And_Location_When_Both_Specified()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, Now, managerId: Guid.NewGuid(), locationId: Guid.NewGuid());
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var newManagerId = Guid.NewGuid();
        var newLocationId = Guid.NewGuid();
        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employee.Id, Guid.NewGuid(), newManagerId: newManagerId, newLocationId: newLocationId),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(newManagerId, savedEmployee.ManagerId);
        Assert.Equal(newLocationId, savedEmployee.LocationId);
    }
}
