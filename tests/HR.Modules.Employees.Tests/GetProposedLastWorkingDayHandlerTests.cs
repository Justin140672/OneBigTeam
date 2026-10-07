using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetProposedLastWorkingDay;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetProposedLastWorkingDayHandlerTests
{
    private static readonly DateOnly Friday = new(2026, 10, 9);
    private static readonly DateOnly Saturday = Friday.AddDays(1);

    private static async Task<Employee> AddEmployeeAsync(EmployeesDbContext context, Guid companyId)
    {
        var now = new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);
        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2026, 7, 1), hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();
        return employee;
    }

    private static GetProposedLastWorkingDayRequest BuildRequest(Guid companyId, Guid employeeId, DateOnly leavingDate) => new()
    {
        CompanyId = companyId,
        EmployeeId = employeeId,
        LeavingDate = leavingDate
    };

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var patterns = new StubWorkingPatternProvider(WorkingPattern.Default);
        var holidays = new StubPublicHolidayReader([]);
        var handler = new GetProposedLastWorkingDayHandler(context, patterns, holidays);

        var result = await handler.HandleAsync(BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Saturday), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Equal(0, patterns.CallCount);
        Assert.Equal(0, holidays.CallCount);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Belongs_To_Another_Company()
    {
        await using var context = BuildContext();
        var employee = await AddEmployeeAsync(context, Guid.NewGuid());
        var handler = new GetProposedLastWorkingDayHandler(
            context, new StubWorkingPatternProvider(WorkingPattern.Default), new StubPublicHolidayReader([]));

        var result = await handler.HandleAsync(BuildRequest(Guid.NewGuid(), employee.Id, Saturday), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Friday_For_Saturday_Leaving_Date_With_Default_Pattern()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = await AddEmployeeAsync(context, companyId);
        var handler = new GetProposedLastWorkingDayHandler(
            context, new StubWorkingPatternProvider(WorkingPattern.Default), new StubPublicHolidayReader([]));

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id, Saturday), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Friday, result.Value!.ProposedLastWorkingDay);
    }

    [Fact]
    public async Task HandleAsync_Uses_Pattern_From_Provider_For_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = await AddEmployeeAsync(context, companyId);
        var patterns = new StubWorkingPatternProvider(
            new WorkingPattern(WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday, 7.5m));
        var handler = new GetProposedLastWorkingDayHandler(context, patterns, new StubPublicHolidayReader([]));

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id, Friday), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Friday.AddDays(-2), result.Value!.ProposedLastWorkingDay);
        Assert.Equal((companyId, employee.Id), patterns.LastArguments);
    }

    [Fact]
    public async Task HandleAsync_Skips_Holiday_On_Leaving_Date()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = await AddEmployeeAsync(context, companyId);
        var holidays = new StubPublicHolidayReader([new PublicHolidayDate(Friday, "Holiday")]);
        var handler = new GetProposedLastWorkingDayHandler(
            context, new StubWorkingPatternProvider(WorkingPattern.Default), holidays);

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id, Friday), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Friday.AddDays(-1), result.Value!.ProposedLastWorkingDay);
    }

    [Fact]
    public async Task HandleAsync_Skips_Friday_Holiday_And_Weekend_For_Sunday_Leaving_Date()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = await AddEmployeeAsync(context, companyId);
        var holidays = new StubPublicHolidayReader([new PublicHolidayDate(Friday, "Holiday")]);
        var handler = new GetProposedLastWorkingDayHandler(
            context, new StubWorkingPatternProvider(WorkingPattern.Default), holidays);

        var result = await handler.HandleAsync(BuildRequest(companyId, employee.Id, Friday.AddDays(2)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Friday.AddDays(-1), result.Value!.ProposedLastWorkingDay);
    }

    [Fact]
    public async Task HandleAsync_Queries_Holidays_For_Sixty_Day_Window_Ending_On_Leaving_Date()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = await AddEmployeeAsync(context, companyId);
        var holidays = new StubPublicHolidayReader([]);
        var handler = new GetProposedLastWorkingDayHandler(
            context, new StubWorkingPatternProvider(WorkingPattern.Default), holidays);

        await handler.HandleAsync(BuildRequest(companyId, employee.Id, Saturday), CancellationToken.None);

        Assert.Equal(1, holidays.CallCount);
        Assert.Equal(companyId, holidays.LastCompanyId);
        Assert.Equal(Saturday.AddDays(-60), holidays.LastFrom);
        Assert.Equal(Saturday, holidays.LastTo);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }

    private sealed class StubWorkingPatternProvider(WorkingPattern pattern) : IWorkingPatternProvider
    {
        public int CallCount { get; private set; }
        public (Guid CompanyId, Guid EmployeeId)? LastArguments { get; private set; }

        public Task<WorkingPattern> GetEffectivePatternAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken)
        {
            CallCount++;
            LastArguments = (companyId, employeeId);
            return Task.FromResult(pattern);
        }
    }

    private sealed class StubPublicHolidayReader(IReadOnlyCollection<PublicHolidayDate> holidays) : IPublicHolidayReader
    {
        public int CallCount { get; private set; }
        public Guid? LastCompanyId { get; private set; }
        public DateOnly? LastFrom { get; private set; }
        public DateOnly? LastTo { get; private set; }

        public Task<IReadOnlyCollection<PublicHolidayDate>> GetPublicHolidaysAsync(
            Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            CallCount++;
            LastCompanyId = companyId;
            LastFrom = from;
            LastTo = to;
            return Task.FromResult(holidays);
        }
    }
}
