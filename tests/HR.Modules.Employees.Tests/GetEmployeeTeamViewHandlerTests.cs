using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetEmployeeTeamView;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetEmployeeTeamViewHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static GetEmployeeTeamViewHandler BuildHandler(
        EmployeesDbContext context, EmployeesResourceAuthorizer resourceAuthorizer) =>
        new(context,
            new FakeOnboardingStatusReader(null),
            new FakeProbationStatusReader(null),
            new FakeOffboardingStatusReader(null),
            resourceAuthorizer);

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }

    private static Employee CreateEmployee(Guid companyId, DateTimeOffset now) =>
        Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);

    [Fact]
    public async Task HandleAsync_Returns_Forbidden_For_Unrelated_Caller()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var resourceAuthorizer = new EmployeesResourceAuthorizer(
            new FakeRoleAuthorizationService(), new FakeDirectReportsReader());
        var handler = BuildHandler(context, resourceAuthorizer);

        var result = await handler.HandleAsync(
            new GetEmployeeTeamViewRequest { CompanyId = companyId, Id = employee.Id, CallerEmployeeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Forbidden_For_Self()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var resourceAuthorizer = new EmployeesResourceAuthorizer(
            new FakeRoleAuthorizationService(), new FakeDirectReportsReader());
        var handler = BuildHandler(context, resourceAuthorizer);

        var result = await handler.HandleAsync(
            new GetEmployeeTeamViewRequest { CompanyId = companyId, Id = employee.Id, CallerEmployeeId = employee.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Forbidden_For_Manager_Viewing_A_Former_Employee_Report()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        employee.SetStatusForTesting(EmploymentStatus.FormerEmployee, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var managerId = Guid.NewGuid();
        var resourceAuthorizer = new EmployeesResourceAuthorizer(
            new FakeRoleAuthorizationService(), new FakeDirectReportsReader(employee.Id));
        var handler = BuildHandler(context, resourceAuthorizer);

        var result = await handler.HandleAsync(
            new GetEmployeeTeamViewRequest { CompanyId = companyId, Id = employee.Id, CallerEmployeeId = managerId },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Manager_In_Hierarchy_And_Returns_Operational_Fields()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var employee = CreateEmployee(companyId, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var managerId = Guid.NewGuid();
        var resourceAuthorizer = new EmployeesResourceAuthorizer(
            new FakeRoleAuthorizationService(), new FakeDirectReportsReader(employee.Id));
        var handler = BuildHandler(context, resourceAuthorizer);

        var result = await handler.HandleAsync(
            new GetEmployeeTeamViewRequest { CompanyId = companyId, Id = employee.Id, CallerEmployeeId = managerId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var value = result.Value!;
        Assert.Equal(employee.Id, value.Id);
        Assert.Equal("Alice", value.FirstName);
        Assert.Equal("Smith", value.LastName);
        Assert.Equal("alice@example.com", value.WorkEmail);
        Assert.Equal(StartDate, value.StartDate);
        Assert.Equal(EmploymentStatus.Draft, value.Status);
        Assert.Equal("EMP-0001", value.EmployeeNumber);
    }

    [Fact]
    public void Response_Has_No_Property_For_Any_Sensitive_Field()
    {
        var responseFieldNames = typeof(GetEmployeeTeamViewResponse)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        string[] sensitiveFieldNames =
        [
            "PersonalEmail", "DateOfBirth", "Nationality", "Gender", "GenderOther",
            "PhoneNumber", "HomePhone", "AddressLine1", "AddressLine2", "City", "County",
            "PostCode", "Country", "HasSystemAccess", "WorkingDaysOverride", "HoursPerDayOverride",
            "ContinuousServiceDate", "ProbationEndDate", "LeavingDate", "NoticePeriodUnitOverride",
            "NoticePeriodLengthOverride", "Notes", "EffectiveNoticePeriodUnit",
            "EffectiveNoticePeriodLength", "EffectiveNoticePeriodSource", "Version",
            "CreatedAt", "UpdatedAt",
        ];

        foreach (var sensitiveFieldName in sensitiveFieldNames)
            Assert.DoesNotContain(sensitiveFieldName, responseFieldNames);
    }
}
