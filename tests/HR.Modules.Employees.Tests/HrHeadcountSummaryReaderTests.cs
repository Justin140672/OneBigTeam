using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class HrHeadcountSummaryReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Employee SeedEmployee(
        EmployeesDbContext db,
        Guid companyId,
        DateOnly startDate,
        Guid? departmentId = null,
        Guid? locationId = null,
        Guid? positionProfileId = null,
        Guid? employmentTypeId = null,
        string firstName = "Alice",
        string lastName = "Smith")
    {
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, firstName, lastName, $"{firstName.ToLowerInvariant()}.{Guid.NewGuid():N}@example.com",
            startDate, hasSystemAccess: false, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001",
            employmentTypeId ?? Guid.NewGuid(), departmentId ?? Guid.NewGuid(), locationId ?? Guid.NewGuid(),
            positionProfileId ?? Guid.NewGuid(), Now);

        db.Employees.Add(employee);
        return employee;
    }

    private static void SeedCompensation(
        EmployeesDbContext db, Guid companyId, Guid employeeId, decimal fte, DateOnly effectiveFrom, DateOnly? effectiveTo = null)
    {
        var compensation = Compensation.Create(
            Guid.NewGuid(), companyId, employeeId, effectiveFrom, SalaryType.Annual, 50000m, "GBP",
            hoursPerWeek: null, fte: fte, notes: null, CompensationChangeReason.NewHire, Guid.NewGuid(), Now);

        if (effectiveTo is not null)
            compensation.Close(effectiveTo.Value, Now);

        db.Compensations.Add(compensation);
    }

    [Fact]
    public async Task GetHeadcountSummaryAsync_Counts_Total_Active_FutureStarters_And_Leavers()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var active = SeedEmployee(db, companyId, Today.AddDays(-30));
        active.Activate(Now);

        var futureStarter = SeedEmployee(db, companyId, Today.AddDays(10));

        var leaver = SeedEmployee(db, companyId, Today.AddDays(-100));
        leaver.Activate(Now);
        leaver.UpdateEmploymentDetails(
            "EMP-LEAVER", Guid.NewGuid(), Today.AddDays(-100), null, null, Today.AddDays(-1), null, Now);

        await db.SaveChangesAsync();

        var reader = new HrHeadcountSummaryReader(db);
        var result = await reader.GetHeadcountSummaryAsync(companyId, new ReportFilterCriteria(), CancellationToken.None);

        Assert.Equal(3, result.TotalHeadcount);
        Assert.Equal(2, result.ActiveEmployees);
        Assert.Equal(1, result.FutureStarters);
        Assert.Equal(1, result.Leavers);
    }

    [Fact]
    public async Task GetHeadcountSummaryAsync_TotalFte_Is_A_Genuine_Sum_Not_Equal_To_Headcount()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var employeeOne = SeedEmployee(db, companyId, Today.AddDays(-30));
        var employeeTwo = SeedEmployee(db, companyId, Today.AddDays(-30));
        var employeeWithNoCompensation = SeedEmployee(db, companyId, Today.AddDays(-30));

        SeedCompensation(db, companyId, employeeOne.Id, 1.0m, Today.AddDays(-30));
        SeedCompensation(db, companyId, employeeTwo.Id, 0.5m, Today.AddDays(-30));
        // employeeWithNoCompensation deliberately has no Compensation record.

        await db.SaveChangesAsync();

        var reader = new HrHeadcountSummaryReader(db);
        var result = await reader.GetHeadcountSummaryAsync(companyId, new ReportFilterCriteria(), CancellationToken.None);

        Assert.Equal(3, result.TotalHeadcount);
        Assert.Equal(1.5m, result.TotalFte);

        var itemWithNoCompensation = result.Items.Single(i => i.EmployeeId == employeeWithNoCompensation.Id);
        Assert.Null(itemWithNoCompensation.Fte);
    }

    [Fact]
    public async Task GetHeadcountSummaryAsync_Picks_Current_Compensation_Record_Not_Expired_Or_Future()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = SeedEmployee(db, companyId, Today.AddDays(-365));

        // Expired record (should be ignored).
        SeedCompensation(db, companyId, employee.Id, 0.2m, Today.AddDays(-365), Today.AddDays(-100));
        // Current record (EffectiveFrom in the past, no EffectiveTo).
        SeedCompensation(db, companyId, employee.Id, 0.8m, Today.AddDays(-99));

        await db.SaveChangesAsync();

        var reader = new HrHeadcountSummaryReader(db);
        var result = await reader.GetHeadcountSummaryAsync(companyId, new ReportFilterCriteria(), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(0.8m, item.Fte);
        Assert.Equal(0.8m, result.TotalFte);
    }

    /// <summary>
    /// CompensationRecordWriter's read-then-write overlap check is meant to guarantee at most one
    /// "current" compensation record per employee, but there is no DB-level uniqueness constraint
    /// backing it. Regression test for a bug where two "current" records for the same employee
    /// (a data anomaly) crashed the whole report via ToDictionaryAsync's duplicate-key exception —
    /// the reader must instead resolve deterministically to the most recently effective one.
    /// </summary>
    [Fact]
    public async Task GetHeadcountSummaryAsync_Does_Not_Throw_When_Employee_Has_Multiple_Current_Compensation_Records()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = SeedEmployee(db, companyId, Today.AddDays(-365));

        // Two open-ended (EffectiveTo == null) records both already effective — a data anomaly the
        // writer's overlap check should prevent in normal operation, but the reader must not crash
        // if it happens anyway.
        SeedCompensation(db, companyId, employee.Id, 0.5m, Today.AddDays(-200));
        SeedCompensation(db, companyId, employee.Id, 1.0m, Today.AddDays(-50));

        await db.SaveChangesAsync();

        var reader = new HrHeadcountSummaryReader(db);
        var result = await reader.GetHeadcountSummaryAsync(companyId, new ReportFilterCriteria(), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(1.0m, item.Fte);
        Assert.Equal(1.0m, result.TotalFte);
    }

    [Fact]
    public async Task GetHeadcountSummaryAsync_Ignores_Compensation_Not_Yet_Effective()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employee = SeedEmployee(db, companyId, Today.AddDays(-30));

        // Future-dated compensation, not yet effective.
        SeedCompensation(db, companyId, employee.Id, 1.0m, Today.AddDays(10));

        await db.SaveChangesAsync();

        var reader = new HrHeadcountSummaryReader(db);
        var result = await reader.GetHeadcountSummaryAsync(companyId, new ReportFilterCriteria(), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Null(item.Fte);
        Assert.Equal(0m, result.TotalFte);
    }

    [Fact]
    public async Task GetHeadcountSummaryAsync_Filters_By_DepartmentId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var engineeringId = Guid.NewGuid();
        var salesId = Guid.NewGuid();

        var engineer = SeedEmployee(db, companyId, Today, departmentId: engineeringId);
        SeedEmployee(db, companyId, Today, departmentId: salesId);

        await db.SaveChangesAsync();

        var reader = new HrHeadcountSummaryReader(db);
        var result = await reader.GetHeadcountSummaryAsync(
            companyId, new ReportFilterCriteria(DepartmentId: engineeringId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(engineer.Id, item.EmployeeId);
    }

    [Fact]
    public async Task GetHeadcountSummaryAsync_Filters_By_LocationId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var londonId = Guid.NewGuid();
        var berlinId = Guid.NewGuid();

        var londoner = SeedEmployee(db, companyId, Today, locationId: londonId);
        SeedEmployee(db, companyId, Today, locationId: berlinId);

        await db.SaveChangesAsync();

        var reader = new HrHeadcountSummaryReader(db);
        var result = await reader.GetHeadcountSummaryAsync(
            companyId, new ReportFilterCriteria(LocationId: londonId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(londoner.Id, item.EmployeeId);
    }

    [Fact]
    public async Task GetHeadcountSummaryAsync_Filters_By_EmploymentTypeId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var fullTimeId = Guid.NewGuid();
        var partTimeId = Guid.NewGuid();

        var fullTimer = SeedEmployee(db, companyId, Today, employmentTypeId: fullTimeId);
        SeedEmployee(db, companyId, Today, employmentTypeId: partTimeId);

        await db.SaveChangesAsync();

        var reader = new HrHeadcountSummaryReader(db);
        var result = await reader.GetHeadcountSummaryAsync(
            companyId, new ReportFilterCriteria(EmploymentTypeId: fullTimeId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(fullTimer.Id, item.EmployeeId);
    }

    [Fact]
    public async Task GetHeadcountSummaryAsync_Filters_By_EmployeeStatus()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var active = SeedEmployee(db, companyId, Today);
        active.Activate(Now);

        var draft = SeedEmployee(db, companyId, Today);
        // draft left at default EmploymentStatus.Draft.

        await db.SaveChangesAsync();

        var reader = new HrHeadcountSummaryReader(db);
        var result = await reader.GetHeadcountSummaryAsync(
            companyId, new ReportFilterCriteria(EmployeeStatus: "Active"), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(active.Id, item.EmployeeId);
        Assert.DoesNotContain(result.Items, i => i.EmployeeId == draft.Id);
    }

    [Fact]
    public async Task GetHeadcountSummaryAsync_Ignores_Unparseable_EmployeeStatus_Filter()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        SeedEmployee(db, companyId, Today);

        await db.SaveChangesAsync();

        var reader = new HrHeadcountSummaryReader(db);
        var result = await reader.GetHeadcountSummaryAsync(
            companyId, new ReportFilterCriteria(EmployeeStatus: "NotARealStatus"), CancellationToken.None);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetHeadcountSummaryAsync_Resolves_Department_Location_Position_And_EmploymentType_Names()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, Now);
        var location = Location.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "London", null, Now);
        var employmentType = EmploymentType.Create(Guid.NewGuid(), companyId, "Full Time", null, Now);
        db.Departments.Add(department);
        db.Locations.Add(location);
        db.EmploymentTypes.Add(employmentType);

        var employee = SeedEmployee(
            db, companyId, Today,
            departmentId: department.Id, locationId: location.Id, employmentTypeId: employmentType.Id);

        await db.SaveChangesAsync();

        var reader = new HrHeadcountSummaryReader(db);
        var result = await reader.GetHeadcountSummaryAsync(companyId, new ReportFilterCriteria(), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Engineering", item.Department);
        Assert.Equal("London", item.Location);
        Assert.Equal("Full Time", item.EmploymentType);
        Assert.Equal(employee.Id, item.EmployeeId);
    }

    [Fact]
    public async Task GetHeadcountSummaryAsync_Isolates_By_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        var mine = SeedEmployee(db, companyId, Today);
        SeedEmployee(db, otherCompanyId, Today);

        await db.SaveChangesAsync();

        var reader = new HrHeadcountSummaryReader(db);
        var result = await reader.GetHeadcountSummaryAsync(companyId, new ReportFilterCriteria(), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(mine.Id, item.EmployeeId);
        Assert.Equal(1, result.TotalHeadcount);
    }
}
