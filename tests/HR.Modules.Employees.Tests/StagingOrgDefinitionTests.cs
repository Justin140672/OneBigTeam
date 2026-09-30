using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Services;

namespace HR.Modules.Employees.Tests;

public class StagingOrgDefinitionTests
{
    private static readonly IReadOnlyList<StagingEmployeeDefinition> Employees = StagingOrgDefinition.Employees;

    [Fact]
    public void Employees_HaveExactlyOneRootWithNoManager()
    {
        var roots = Employees.Where(e => e.ManagerNumber is null).ToList();

        var root = Assert.Single(roots);
        Assert.Equal("Sarah Chen", $"{root.FirstName} {root.LastName}");
    }

    [Fact]
    public void Employees_EveryManagerReferenceExists()
    {
        var numbers = Employees.Select(e => e.Number).ToHashSet();

        foreach (var employee in Employees.Where(e => e.ManagerNumber is not null))
        {
            Assert.Contains(employee.ManagerNumber!.Value, numbers);
            Assert.NotEqual(employee.Number, employee.ManagerNumber);
        }
    }

    [Fact]
    public void Employees_HaveNoManagerCycles()
    {
        var managerByNumber = Employees.ToDictionary(e => e.Number, e => e.ManagerNumber);

        foreach (var employee in Employees)
        {
            var visited = new HashSet<int> { employee.Number };
            var current = employee.ManagerNumber;

            while (current is { } managerNumber)
            {
                Assert.True(visited.Add(managerNumber), $"Manager cycle detected starting at employee {employee.Number}.");
                current = managerByNumber[managerNumber];
            }
        }
    }

    [Fact]
    public void Employees_HaveUniqueNumbersAndNames_AndStartAfterTheirManager()
    {
        Assert.Equal(Employees.Count, Employees.Select(e => e.Number).Distinct().Count());
        Assert.Equal(Employees.Count, Employees.Select(e => $"{e.FirstName} {e.LastName}").Distinct().Count());
        Assert.Equal(StagingSeedOptions.NextEmployeeNumber, Employees.Count + 1);

        var byNumber = Employees.ToDictionary(e => e.Number);
        foreach (var employee in Employees.Where(e => e.ManagerNumber is not null))
        {
            Assert.True(employee.StartDate >= byNumber[employee.ManagerNumber!.Value].StartDate);
        }
    }

    [Fact]
    public void Employees_ReferenceOnlyDefinedPositionsAndEmploymentTypes()
    {
        var positionNames = StagingOrgDefinition.Positions.Select(p => p.Name).ToHashSet();
        var departmentNames = StagingOrgDefinition.Departments.Select(d => d.Name).ToHashSet();

        Assert.All(Employees, e => Assert.Contains(e.PositionName, positionNames));
        Assert.All(Employees, e => Assert.Contains(e.EmploymentTypeName, StagingOrgDefinition.EmploymentTypeNames));
        Assert.All(StagingOrgDefinition.Positions, p => Assert.Contains(p.DepartmentName, departmentNames));
    }

    [Fact]
    public void PositionProfiles_HaveAtMostOneEmployeeAndExactlyThreeAreVacant()
    {
        var duplicated = Employees.GroupBy(e => e.PositionName).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicated);

        var vacant = StagingOrgDefinition.Positions
            .Select(p => p.Name)
            .Where(name => Employees.All(e => e.PositionName != name))
            .OrderBy(name => name)
            .ToList();

        Assert.Equal(26, StagingOrgDefinition.Positions.Count);
        Assert.Equal(new[] { "Data Analyst", "Marketing Coordinator", "Software Engineer" }, vacant);
        Assert.Equal(StagingOrgDefinition.VacantPositionNames.OrderBy(n => n), vacant);
    }

    [Theory]
    [InlineData("David Park", "Sales Manager")]
    [InlineData("James Okafor", "Chief Technology Officer")]
    public void VacancyHiringManagers_Exist(string name, string positionName)
    {
        var employee = Assert.Single(Employees, e => $"{e.FirstName} {e.LastName}" == name);
        Assert.Equal(positionName, employee.PositionName);
        Assert.Contains(StagingOrgDefinition.Positions, p => p.Name == "Software Engineer");
    }
}
