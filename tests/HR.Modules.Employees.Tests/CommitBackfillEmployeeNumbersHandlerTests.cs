using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CommitBackfillEmployeeNumbers;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HR.Modules.Employees.Tests;

public class CommitBackfillEmployeeNumbersHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ActorEmployeeId = Guid.NewGuid();

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new EmployeesDbContext(options);
    }

    private static CommitBackfillEmployeeNumbersHandler BuildHandler(
        EmployeesDbContext context,
        FakeCompanyEmployeeNumberSettingsReader? settingsReader = null,
        FakeEmployeeNumberGenerator? generator = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(
            context,
            settingsReader ?? new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Automatic),
            generator ?? new FakeEmployeeNumberGenerator(),
            new FakeClock(FixedUtcNow),
            auditPublisher ?? new FakeAuditPublisher());

    private static Employee CreateEmployee(
        Guid companyId, string firstName, string lastName, DateOnly startDate, string employeeNumber, DateTimeOffset now) =>
        Employee.Create(
            Guid.NewGuid(), companyId, firstName, lastName, $"{firstName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
            startDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say",
            employeeNumber, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Company_Is_Not_In_Automatic_Mode()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var handler = BuildHandler(
            context, new FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Manual));

        var result = await handler.HandleAsync(
            new CommitBackfillEmployeeNumbersRequest(companyId), ActorEmployeeId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Assigns_Numbers_Only_To_Employees_Missing_One_In_StartDate_Then_Name_Order()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var alreadyNumbered = CreateEmployee(companyId, "Zack", "Adams", new DateOnly(2023, 1, 1), "EMP-EXISTING", now);
        var second = CreateEmployee(companyId, "Bob", "Jones", new DateOnly(2024, 2, 1), "", now);
        var first = CreateEmployee(companyId, "Alice", "Smith", new DateOnly(2024, 1, 1), "", now);
        context.Employees.AddRange(alreadyNumbered, second, first);
        await context.SaveChangesAsync();

        var generator = new FakeEmployeeNumberGenerator(counter => $"AUTO-{counter:D5}");
        var handler = BuildHandler(context, generator: generator);

        var result = await handler.HandleAsync(
            new CommitBackfillEmployeeNumbersRequest(companyId), ActorEmployeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Equal(first.Id, result.Value.Items[0].EmployeeId);
        Assert.Equal("AUTO-00001", result.Value.Items[0].AssignedEmployeeNumber);
        Assert.Equal(second.Id, result.Value.Items[1].EmployeeId);
        Assert.Equal("AUTO-00002", result.Value.Items[1].AssignedEmployeeNumber);

        var savedAlreadyNumbered = await context.Employees.SingleAsync(e => e.Id == alreadyNumbered.Id);
        Assert.Equal("EMP-EXISTING", savedAlreadyNumbered.EmployeeNumber);

        var savedFirst = await context.Employees.SingleAsync(e => e.Id == first.Id);
        Assert.Equal("AUTO-00001", savedFirst.EmployeeNumber);

        var savedSecond = await context.Employees.SingleAsync(e => e.Id == second.Id);
        Assert.Equal("AUTO-00002", savedSecond.EmployeeNumber);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_Result_When_No_Employees_Are_Missing_A_Number()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        context.Employees.Add(CreateEmployee(companyId, "Alice", "Smith", new DateOnly(2024, 1, 1), "EMP-0001", now));
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            new CommitBackfillEmployeeNumbersRequest(companyId), ActorEmployeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.TotalCount);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task HandleAsync_Publishes_One_AuditEvent_Per_Assigned_Employee_With_Shared_BackfillOperationId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var first = CreateEmployee(companyId, "Alice", "Smith", new DateOnly(2024, 1, 1), "", now);
        var second = CreateEmployee(companyId, "Bob", "Jones", new DateOnly(2024, 2, 1), "", now);
        context.Employees.AddRange(first, second);
        await context.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, auditPublisher: auditPublisher);

        var result = await handler.HandleAsync(
            new CommitBackfillEmployeeNumbersRequest(companyId), ActorEmployeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, auditPublisher.Published.Count);
        Assert.All(auditPublisher.Published, e => Assert.IsType<EmployeeNumberBackfilledAuditEvent>(e));
        var events = auditPublisher.Published.Cast<EmployeeNumberBackfilledAuditEvent>().ToList();
        Assert.All(events, e => Assert.Equal(result.Value!.BackfillOperationId, e.BackfillOperationId));
        Assert.All(events, e => Assert.Equal(ActorEmployeeId, e.ActorEmployeeId));
        Assert.All(events, e => Assert.Equal(companyId, e.CompanyId));
        Assert.Contains(events, e => e.EmployeeId == first.Id && e.AssignedEmployeeNumber == "AUTO-00001");
        Assert.Contains(events, e => e.EmployeeId == second.Id && e.AssignedEmployeeNumber == "AUTO-00002");
    }

    [Fact]
    public async Task HandleAsync_Only_Affects_Employees_Scoped_To_The_Requested_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var ownEmployee = CreateEmployee(companyId, "Alice", "Smith", new DateOnly(2024, 1, 1), "", now);
        var otherEmployee = CreateEmployee(otherCompanyId, "Bob", "Jones", new DateOnly(2024, 1, 1), "", now);
        context.Employees.AddRange(ownEmployee, otherEmployee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var result = await handler.HandleAsync(
            new CommitBackfillEmployeeNumbersRequest(companyId), ActorEmployeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(ownEmployee.Id, item.EmployeeId);

        var savedOther = await context.Employees.SingleAsync(e => e.Id == otherEmployee.Id);
        Assert.Equal("", savedOther.EmployeeNumber);
    }
}
