using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetEmploymentTypeSplit;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class EmploymentTypeGroupingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 1, 1);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Employee NewActiveEmployee(Guid companyId, string firstName, Guid employmentTypeId)
    {
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, firstName, "Tester", $"{firstName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
            StartDate, hasSystemAccess: false, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001",
            employmentTypeId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        employee.Activate(Now);
        return employee;
    }

    [Fact]
    public async Task Dashboard_And_Report_Produce_Identical_Groupings_For_The_Same_Population()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var permanent = EmploymentType.Create(Guid.NewGuid(), companyId, "Permanent", null, Now);
        var fixedTerm = EmploymentType.Create(Guid.NewGuid(), companyId, "Fixed Term", null, Now);
        db.EmploymentTypes.AddRange(permanent, fixedTerm);
        db.Employees.AddRange(
            NewActiveEmployee(companyId, "A", permanent.Id),
            NewActiveEmployee(companyId, "B", permanent.Id),
            NewActiveEmployee(companyId, "C", fixedTerm.Id));
        await db.SaveChangesAsync();

        var dashboard = await new GetEmploymentTypeSplitHandler(db)
            .HandleAsync(new GetEmploymentTypeSplitRequest(companyId), CancellationToken.None);
        var report = await new HrHeadcountSummaryReader(db)
            .GetHeadcountSummaryAsync(companyId, new ReportFilterCriteria(), CancellationToken.None);

        var dashboardGroups = dashboard.Items
            .Select(i => (i.EmploymentTypeId!.Value, i.EmploymentTypeName, i.EmployeeCount))
            .OrderBy(g => g.Item1).ToList();
        var reportGroups = report.EmploymentTypeBreakdown!
            .Select(g => (g.EmploymentTypeId, g.Label, g.EmployeeCount))
            .OrderBy(g => g.Item1).ToList();

        Assert.Equal(dashboardGroups, reportGroups);
        Assert.DoesNotContain(reportGroups, g => g.Label == "Full Time");
    }

    [Fact]
    public async Task Inactive_Imported_And_Missing_Types_Are_Flagged_Not_Presented_As_Canonical()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var configured = EmploymentType.Create(Guid.NewGuid(), companyId, "Permanent", null, Now);
        var imported = EmploymentType.CreateFromImport(Guid.NewGuid(), companyId, "Full Time", Now);
        var inactive = EmploymentType.Create(Guid.NewGuid(), companyId, "Casual", null, Now);
        inactive.Deactivate(Now);
        var missingId = Guid.NewGuid();
        db.EmploymentTypes.AddRange(configured, imported, inactive);
        db.Employees.AddRange(
            NewActiveEmployee(companyId, "A", configured.Id),
            NewActiveEmployee(companyId, "B", imported.Id),
            NewActiveEmployee(companyId, "C", inactive.Id),
            NewActiveEmployee(companyId, "D", missingId));
        await db.SaveChangesAsync();

        var report = await new HrHeadcountSummaryReader(db)
            .GetHeadcountSummaryAsync(companyId, new ReportFilterCriteria(), CancellationToken.None);

        var byId = report.EmploymentTypeBreakdown!.ToDictionary(g => g.EmploymentTypeId);
        Assert.False(byId[configured.Id].IsNonCanonical);
        Assert.Equal("Full Time (unreviewed import)", byId[imported.Id].Label);
        Assert.True(byId[imported.Id].IsNonCanonical);
        Assert.Equal("Casual (inactive)", byId[inactive.Id].Label);
        Assert.Equal(EmploymentTypeGrouping.UnknownLabel, byId[missingId].Label);
        Assert.True(byId[missingId].IsNonCanonical);
        Assert.Equal(3, report.Items.Count(i => i.EmploymentTypeNeedsReview));
    }

    [Fact]
    public void Updating_An_Imported_Type_Marks_It_Reviewed()
    {
        var imported = EmploymentType.CreateFromImport(Guid.NewGuid(), Guid.NewGuid(), "Full Time", Now);
        Assert.True(imported.RequiresReview);

        imported.Update("Full Time", null, Now);

        Assert.False(imported.RequiresReview);
    }

    [Fact]
    public async Task Grouping_Is_By_Id_So_Renamed_Types_Do_Not_Split_Groups()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = EmploymentType.Create(Guid.NewGuid(), companyId, "Permanent", null, Now);
        db.EmploymentTypes.Add(type);
        db.Employees.AddRange(NewActiveEmployee(companyId, "A", type.Id), NewActiveEmployee(companyId, "B", type.Id));
        await db.SaveChangesAsync();
        type.Update("Permanent Staff", null, Now);
        await db.SaveChangesAsync();

        var dashboard = await new GetEmploymentTypeSplitHandler(db)
            .HandleAsync(new GetEmploymentTypeSplitRequest(companyId), CancellationToken.None);

        var item = Assert.Single(dashboard.Items);
        Assert.Equal("Permanent Staff", item.EmploymentTypeName);
        Assert.Equal(2, item.EmployeeCount);
    }
}
