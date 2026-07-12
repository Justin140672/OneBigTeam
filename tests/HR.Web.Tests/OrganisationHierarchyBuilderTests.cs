using HR.Web.Models;
using HR.Web.Services;

namespace HR.Web.Tests;

public class OrganisationHierarchyBuilderTests
{
    private readonly OrganisationHierarchyBuilder _builder = new();

    private static OrganisationChartEmployeeModel Employee(Guid id, string name, Guid? managerId = null) =>
        new(id, name, "EMP-0001", "Job Title", "Department", managerId, "Location", null);

    [Fact]
    public void Build_Returns_Empty_For_No_Employees()
    {
        var result = _builder.Build([]);

        Assert.Empty(result);
    }

    [Fact]
    public void Build_Employee_Without_Manager_Becomes_Root_Node()
    {
        var alice = Employee(Guid.NewGuid(), "Alice");

        var result = _builder.Build([alice]);

        var root = Assert.Single(result);
        Assert.Equal(alice.EmployeeId, root.EmployeeId);
        Assert.Empty(root.DirectReports);
    }

    [Fact]
    public void Build_Multiple_Employees_Without_Managers_All_Become_Root_Nodes()
    {
        var alice = Employee(Guid.NewGuid(), "Alice");
        var bob = Employee(Guid.NewGuid(), "Bob");

        var result = _builder.Build([alice, bob]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, n => n.EmployeeId == alice.EmployeeId);
        Assert.Contains(result, n => n.EmployeeId == bob.EmployeeId);
    }

    [Fact]
    public void Build_Employee_With_Manager_Not_In_The_Set_Becomes_A_Root_Node()
    {
        // ManagerId points at someone who isn't part of this chart (e.g. inactive/left) — treated
        // the same as having no manager at all, rather than being dropped.
        var alice = Employee(Guid.NewGuid(), "Alice", managerId: Guid.NewGuid());

        var result = _builder.Build([alice]);

        var root = Assert.Single(result);
        Assert.Equal(alice.EmployeeId, root.EmployeeId);
    }

    [Fact]
    public void Build_Supports_Multiple_Levels()
    {
        var ceo = Employee(Guid.NewGuid(), "Carol CEO");
        var manager = Employee(Guid.NewGuid(), "Mia Manager", managerId: ceo.EmployeeId);
        var ic = Employee(Guid.NewGuid(), "Ivan IC", managerId: manager.EmployeeId);

        var result = _builder.Build([ceo, manager, ic]);

        var root = Assert.Single(result);
        Assert.Equal(ceo.EmployeeId, root.EmployeeId);

        var managerNode = Assert.Single(root.DirectReports);
        Assert.Equal(manager.EmployeeId, managerNode.EmployeeId);

        var icNode = Assert.Single(managerNode.DirectReports);
        Assert.Equal(ic.EmployeeId, icNode.EmployeeId);
        Assert.Empty(icNode.DirectReports);
    }

    [Fact]
    public void Build_Groups_Multiple_Direct_Reports_Under_The_Same_Manager()
    {
        var manager = Employee(Guid.NewGuid(), "Mia Manager");
        var report1 = Employee(Guid.NewGuid(), "Alice Report", managerId: manager.EmployeeId);
        var report2 = Employee(Guid.NewGuid(), "Bob Report", managerId: manager.EmployeeId);

        var result = _builder.Build([manager, report1, report2]);

        var root = Assert.Single(result);
        Assert.Equal(2, root.DirectReports.Count);
        Assert.Contains(root.DirectReports, n => n.EmployeeId == report1.EmployeeId);
        Assert.Contains(root.DirectReports, n => n.EmployeeId == report2.EmployeeId);
    }

    [Fact]
    public void Build_Two_Node_Circular_Reference_Does_Not_Hang_And_Includes_Both_Employees()
    {
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var alice = Employee(aliceId, "Alice", managerId: bobId);
        var bob = Employee(bobId, "Bob", managerId: aliceId);

        var result = _builder.Build([alice, bob]);

        var allNodeIds = Flatten(result).Select(n => n.EmployeeId).ToList();
        Assert.Contains(aliceId, allNodeIds);
        Assert.Contains(bobId, allNodeIds);
        // Exactly one root is promoted to break the cycle — the other hangs beneath it rather
        // than also appearing as a second, disconnected root.
        Assert.Single(result);
    }

    [Fact]
    public void Build_Longer_Circular_Reference_Does_Not_Hang_And_Includes_All_Employees()
    {
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();
        var cId = Guid.NewGuid();
        var a = Employee(aId, "A", managerId: cId);
        var b = Employee(bId, "B", managerId: aId);
        var c = Employee(cId, "C", managerId: bId);

        var result = _builder.Build([a, b, c]);

        var allNodeIds = Flatten(result).Select(n => n.EmployeeId).ToHashSet();
        Assert.Equal(3, allNodeIds.Count);
        Assert.Contains(aId, allNodeIds);
        Assert.Contains(bId, allNodeIds);
        Assert.Contains(cId, allNodeIds);
    }

    [Fact]
    public void Build_Self_Referencing_Manager_Does_Not_Hang()
    {
        var aliceId = Guid.NewGuid();
        var alice = Employee(aliceId, "Alice", managerId: aliceId);

        var result = _builder.Build([alice]);

        var root = Assert.Single(result);
        Assert.Equal(aliceId, root.EmployeeId);
        Assert.Empty(root.DirectReports);
    }

    [Fact]
    public void Build_Mixed_Tree_With_Independent_Cycle_Keeps_Genuine_Tree_Intact()
    {
        var ceo = Employee(Guid.NewGuid(), "Carol CEO");
        var report = Employee(Guid.NewGuid(), "Rick Report", managerId: ceo.EmployeeId);

        var xId = Guid.NewGuid();
        var yId = Guid.NewGuid();
        var x = Employee(xId, "X", managerId: yId);
        var y = Employee(yId, "Y", managerId: xId);

        var result = _builder.Build([ceo, report, x, y]);

        var ceoRoot = Assert.Single(result, n => n.EmployeeId == ceo.EmployeeId);
        Assert.Single(ceoRoot.DirectReports, n => n.EmployeeId == report.EmployeeId);

        var allNodeIds = Flatten(result).Select(n => n.EmployeeId).ToHashSet();
        Assert.Contains(xId, allNodeIds);
        Assert.Contains(yId, allNodeIds);
    }

    private static IEnumerable<OrganisationChartNode> Flatten(IEnumerable<OrganisationChartNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var descendant in Flatten(node.DirectReports))
                yield return descendant;
        }
    }
}
