using HR.Modules.Probation.Tests.Infrastructure;

namespace HR.Modules.Probation.Tests;

/// <summary>
/// DSH-02: the test double's hierarchy mode must be a real BFS closure with a visited-set so that
/// cyclic / self-referential reporting-line inputs terminate, and re-parenting is reflected on the
/// next read. See specifications/architecture/11-manager-hierarchy-scope.md.
/// </summary>
public class FakeDirectReportsReaderTests
{
    private static readonly Guid Company = Guid.NewGuid();

    [Fact]
    public async Task GetAllDescendantIdsAsync_Returns_The_Transitive_Closure()
    {
        var top = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var leaf = Guid.NewGuid();
        var reader = FakeDirectReportsReader.WithHierarchy((top, mid), (mid, leaf));

        var all = await reader.GetAllDescendantIdsAsync(Company, top, CancellationToken.None);

        Assert.Equal(new HashSet<Guid> { mid, leaf }, all.ToHashSet());
    }

    [Fact]
    public async Task GetDirectReportIdsAsync_Returns_Only_Immediate_Reports()
    {
        var top = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var leaf = Guid.NewGuid();
        var reader = FakeDirectReportsReader.WithHierarchy((top, mid), (mid, leaf));

        var direct = await reader.GetDirectReportIdsAsync(Company, top, CancellationToken.None);

        Assert.Equal(new[] { mid }, direct);
    }

    [Fact]
    public async Task GetAllDescendantIdsAsync_Terminates_On_Two_Node_Cycle_Without_Duplicates()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var reader = FakeDirectReportsReader.WithHierarchy((a, b), (b, a));

        var all = await reader.GetAllDescendantIdsAsync(Company, a, CancellationToken.None);

        Assert.Equal(new[] { b }, all);
    }

    [Fact]
    public async Task GetAllDescendantIdsAsync_Terminates_On_Self_Referential_Manager()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var reader = FakeDirectReportsReader.WithHierarchy((a, a), (a, b));

        var all = await reader.GetAllDescendantIdsAsync(Company, a, CancellationToken.None);

        Assert.Equal(new[] { b }, all);
    }

    [Fact]
    public async Task Reparent_Moves_Employee_Between_Manager_Subtrees_On_Next_Read()
    {
        var managerX = Guid.NewGuid();
        var managerY = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var reader = FakeDirectReportsReader.WithHierarchy((managerX, employee));

        Assert.Contains(employee, await reader.GetAllDescendantIdsAsync(Company, managerX, CancellationToken.None));

        reader.Reparent(employee, managerY);

        Assert.DoesNotContain(employee, await reader.GetAllDescendantIdsAsync(Company, managerX, CancellationToken.None));
        Assert.Contains(employee, await reader.GetAllDescendantIdsAsync(Company, managerY, CancellationToken.None));
    }
}
