using ClosedXML.Excel;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetCompensationImportTemplate;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class GetCompensationImportTemplateHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    private static GetCompensationImportTemplateHandler BuildHandler(EmployeesDbContext context) =>
        new(context, new FakeClock(FixedUtcNow), new FakeCompanyTimeZoneReader());

    [Fact]
    public async Task GenerateAsync_Includes_Active_Employees_With_Current_Compensation()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);

        var current = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2025, 1, 1), SalaryType.Annual, 45000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        context.Compensations.Add(current);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var bytes = await handler.GenerateAsync(companyId, CancellationToken.None);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(CompensationImportTemplateBuilder.SheetName);

        Assert.Equal("EMP-0001", sheet.Cell(2, 1).GetString());
        Assert.Equal("Alice Smith", sheet.Cell(2, 2).GetString());
        Assert.Equal(45000m, sheet.Cell(2, 3).GetValue<decimal>());
        Assert.Equal("Annual", sheet.Cell(2, 4).GetString());
    }

    [Fact]
    public async Task GenerateAsync_Excludes_Former_Employees()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2024, 1, 1), true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        employee.SetStatusForTesting(EmploymentStatus.FormerEmployee, now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var bytes = await handler.GenerateAsync(companyId, CancellationToken.None);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(CompensationImportTemplateBuilder.SheetName);

        Assert.True(sheet.Cell(2, 1).IsEmpty());
    }

    [Fact]
    public async Task GenerateAsync_Leaves_Reference_Columns_Blank_When_Employee_Has_No_Compensation_Record()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Charlie", "Brown", "charlie@example.com", new DateOnly(2024, 1, 1), true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0002", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);

        var bytes = await handler.GenerateAsync(companyId, CancellationToken.None);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(CompensationImportTemplateBuilder.SheetName);

        Assert.Equal("EMP-0002", sheet.Cell(2, 1).GetString());
        Assert.True(sheet.Cell(2, 3).IsEmpty());
        Assert.True(sheet.Cell(2, 4).IsEmpty());
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
