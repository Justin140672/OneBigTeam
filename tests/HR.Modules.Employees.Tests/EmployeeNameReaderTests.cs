using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class EmployeeNameReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Employee NewEmployee(Guid companyId, string first, string last, string? preferred)
    {
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, first, last, $"{first}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
            new DateOnly(2026, 1, 1), hasSystemAccess: false, new DateOnly(1990, 1, 1), "British",
            "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        employee.UpdatePersonalDetails(preferred, new DateOnly(1990, 1, 1), "British", "Prefer not to say", null, Now);
        return employee;
    }

    [Fact]
    public async Task GetNamesAsync_Uses_Preferred_Name_When_Present_And_Legal_First_Name_Otherwise()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var withPreferred = NewEmployee(companyId, "Sara", "Chen", "Sarah");
        var withoutPreferred = NewEmployee(companyId, "Bob", "Jones", null);
        db.Employees.AddRange(withPreferred, withoutPreferred);
        await db.SaveChangesAsync();

        var names = await new EmployeeNameReader(db).GetNamesAsync(
            companyId, [withPreferred.Id, withoutPreferred.Id], CancellationToken.None);

        Assert.Equal("Sarah Chen", names[withPreferred.Id]);
        Assert.Equal("Bob Jones", names[withoutPreferred.Id]);
    }

    [Fact]
    public async Task HrHeadcountSummary_And_NameReader_Agree_On_Display_Name()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = NewEmployee(companyId, "Sara", "Chen", "Sarah");
        employee.Activate(Now);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        var names = await new EmployeeNameReader(db).GetNamesAsync(companyId, [employee.Id], CancellationToken.None);
        var report = await new HrHeadcountSummaryReader(db).GetHeadcountSummaryAsync(
            companyId, new HR.Infrastructure.Abstractions.ReportFilterCriteria(), CancellationToken.None);

        Assert.Equal(names[employee.Id], Assert.Single(report.Items).EmployeeName);
    }
}
