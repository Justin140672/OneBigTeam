using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Jobs;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Employees.Tests;

public class ProcessPromotionsJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 25, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Employee CreateEmployee(
        Guid companyId, DateTimeOffset now, Guid? positionProfileId = null)
    {
        return Employee.Create(
            Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate,
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), positionProfileId ?? Guid.NewGuid(), now);
    }

    private static EmployeePromotion CreatePromotion(
        Guid companyId,
        Guid employeeId,
        DateOnly effectiveDate,
        DateTimeOffset now,
        Guid? previousPositionProfileId = null,
        Guid? newPositionProfileId = null) =>
        EmployeePromotion.Create(
            Guid.NewGuid(), companyId, employeeId,
            previousPositionProfileId ?? Guid.NewGuid(), newPositionProfileId ?? Guid.NewGuid(),
            newManagerId: null, newLocationId: null, effectiveDate,
            "Promotion for excellent performance.", notes: null, compensationId: null,
            createdBy: Guid.NewGuid(), now);

    private static ProcessPromotionsJob BuildJob(
        EmployeesDbContext dbContext,
        FakeAuditPublisher? auditPublisher = null,
        CapturingIntegrationEventPublisher? integrationEventPublisher = null,
        DateTime? fixedUtcNow = null,
        ICompanyTimeZoneReader? companyTimeZoneReader = null,
        NullLogger<ProcessPromotionsJob>? logger = null)
    {
        var finalizer = new EmployeePromotionFinalizer(
            dbContext,
            auditPublisher ?? new FakeAuditPublisher(),
            integrationEventPublisher ?? new CapturingIntegrationEventPublisher());

        return new(
            dbContext,
            new FakeClock(fixedUtcNow ?? FixedUtcNow),
            companyTimeZoneReader ?? new FakeCompanyTimeZoneReader(),
            finalizer,
            NullLogger<ProcessPromotionsJob>.Instance);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task ExecuteAsync_Finalizes_Promotion_When_EffectiveDate_Has_Passed_Or_Is_Today(
        int effectiveDateOffsetDays)
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateEmployee(companyId, Now);
        context.Employees.Add(employee);
        var promotion = CreatePromotion(companyId, employee.Id, Today.AddDays(effectiveDateOffsetDays), Now);
        context.EmployeePromotions.Add(promotion);
        await context.SaveChangesAsync();

        var job = BuildJob(context);

        await job.ExecuteAsync();

        var savedPromotion = await context.EmployeePromotions.SingleAsync();
        Assert.NotNull(savedPromotion.CompletedAt);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(promotion.NewPositionProfileId, savedEmployee.PositionProfileId);
    }

    [Fact]
    public async Task ExecuteAsync_Leaves_Future_Dated_Promotion_Untouched()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var originalPositionProfileId = Guid.NewGuid();

        var employee = CreateEmployee(companyId, Now, positionProfileId: originalPositionProfileId);
        context.Employees.Add(employee);
        var promotion = CreatePromotion(companyId, employee.Id, Today.AddDays(1), Now);
        context.EmployeePromotions.Add(promotion);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var job = BuildJob(context, auditPublisher: auditPublisher);

        await job.ExecuteAsync();

        var savedPromotion = await context.EmployeePromotions.SingleAsync();
        Assert.Null(savedPromotion.CompletedAt);

        var savedEmployee = await context.Employees.SingleAsync();
        Assert.Equal(originalPositionProfileId, savedEmployee.PositionProfileId);

        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task ExecuteAsync_Excludes_Already_Completed_Promotions()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = CreateEmployee(companyId, Now);
        context.Employees.Add(employee);
        var promotion = CreatePromotion(companyId, employee.Id, Today.AddDays(-5), Now);
        promotion.Complete(Now.AddDays(-4));
        context.EmployeePromotions.Add(promotion);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var job = BuildJob(context, auditPublisher: auditPublisher);

        var exception = await Record.ExceptionAsync(() => job.ExecuteAsync());

        Assert.Null(exception);
        Assert.Empty(auditPublisher.Published);

        var savedPromotion = await context.EmployeePromotions.SingleAsync();
        Assert.Equal(Now.AddDays(-4), savedPromotion.CompletedAt);
    }

    [Fact]
    public async Task ExecuteAsync_Skips_Promotion_Without_Throwing_When_Employee_Not_Found()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        // Inconsistent state: promotion references an employee that does not exist.
        var missingEmployeePromotion = CreatePromotion(companyId, Guid.NewGuid(), Today.AddDays(-1), Now);
        context.EmployeePromotions.Add(missingEmployeePromotion);

        // A normal, correctly-due promotion in the same run to prove it is unaffected.
        var okEmployee = CreateEmployee(companyId, Now);
        context.Employees.Add(okEmployee);
        var okPromotion = CreatePromotion(companyId, okEmployee.Id, Today.AddDays(-1), Now);
        context.EmployeePromotions.Add(okPromotion);

        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var job = BuildJob(context, auditPublisher: auditPublisher);

        var exception = await Record.ExceptionAsync(() => job.ExecuteAsync());

        Assert.Null(exception);

        var savedMissingEmployeePromotion = await context.EmployeePromotions
            .SingleAsync(p => p.Id == missingEmployeePromotion.Id);
        Assert.Null(savedMissingEmployeePromotion.CompletedAt);

        var savedOkPromotion = await context.EmployeePromotions.SingleAsync(p => p.Id == okPromotion.Id);
        Assert.NotNull(savedOkPromotion.CompletedAt);

        var auditEvent = Assert.IsType<EmployeePromotionCompletedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(okPromotion.Id, auditEvent.PromotionId);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Early_When_No_Pending_Promotions()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        // A completed promotion exists, but nothing pending — the query should exclude it and the
        // job should do no work at all.
        var employee = CreateEmployee(companyId, Now);
        context.Employees.Add(employee);
        var promotion = CreatePromotion(companyId, employee.Id, Today.AddDays(-1), Now);
        promotion.Complete(Now);
        context.EmployeePromotions.Add(promotion);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var integrationEventPublisher = new CapturingIntegrationEventPublisher();
        var job = BuildJob(context, auditPublisher: auditPublisher, integrationEventPublisher: integrationEventPublisher);

        await job.ExecuteAsync();

        Assert.Empty(auditPublisher.Published);
        Assert.Empty(integrationEventPublisher.Published);
    }

    [Fact]
    public async Task ExecuteAsync_Resolves_Due_Date_Independently_Per_Company_Time_Zone()
    {
        // 2026-07-25T23:30:00Z is still 2026-07-25 in UTC, but already 2026-07-26 00:30 in
        // Europe/London (BST, UTC+1). A promotion effective 2026-07-26 is due in a UK company but
        // not yet due in a UTC company at the same instant.
        var fixedUtcNow = new DateTime(2026, 7, 25, 23, 30, 0, DateTimeKind.Utc);
        var localNow = new DateTimeOffset(fixedUtcNow, TimeSpan.Zero);

        await using var context = BuildContext();
        var ukCompanyId = Guid.NewGuid();
        var utcCompanyId = Guid.NewGuid();

        var ukEmployee = CreateEmployee(ukCompanyId, localNow);
        context.Employees.Add(ukEmployee);
        var ukPromotion = CreatePromotion(ukCompanyId, ukEmployee.Id, new DateOnly(2026, 7, 26), localNow);
        context.EmployeePromotions.Add(ukPromotion);

        var utcEmployee = CreateEmployee(utcCompanyId, localNow);
        context.Employees.Add(utcEmployee);
        var utcPromotion = CreatePromotion(utcCompanyId, utcEmployee.Id, new DateOnly(2026, 7, 26), localNow);
        context.EmployeePromotions.Add(utcPromotion);

        await context.SaveChangesAsync();

        var job = BuildJob(
            context,
            fixedUtcNow: fixedUtcNow,
            companyTimeZoneReader: new PerCompanyTimeZoneReader(
                new Dictionary<Guid, string>
                {
                    [ukCompanyId] = "Europe/London",
                    [utcCompanyId] = "UTC",
                }));

        await job.ExecuteAsync();

        var savedUkPromotion = await context.EmployeePromotions.SingleAsync(p => p.Id == ukPromotion.Id);
        Assert.NotNull(savedUkPromotion.CompletedAt);

        var savedUtcPromotion = await context.EmployeePromotions.SingleAsync(p => p.Id == utcPromotion.Id);
        Assert.Null(savedUtcPromotion.CompletedAt);
    }
}
