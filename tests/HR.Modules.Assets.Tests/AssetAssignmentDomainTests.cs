using HR.Modules.Assets.Domain;

namespace HR.Modules.Assets.Tests;

public class AssetAssignmentDomainTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    private static AssetAssignment CreateAssignment() => AssetAssignment.Create(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        null,
        FixedNow);

    [Fact]
    public void Acknowledge_Sets_AcknowledgedAt_When_Not_Previously_Acknowledged()
    {
        var assignment = CreateAssignment();
        var acknowledgedAt = FixedNow.AddDays(1);

        assignment.Acknowledge(acknowledgedAt);

        Assert.Equal(acknowledgedAt, assignment.AcknowledgedAt);
        Assert.Equal(acknowledgedAt, assignment.UpdatedAt);
    }

    [Fact]
    public void Acknowledge_Throws_When_Already_Acknowledged()
    {
        var assignment = CreateAssignment();
        assignment.Acknowledge(FixedNow.AddDays(1));

        Assert.Throws<InvalidOperationException>(() => assignment.Acknowledge(FixedNow.AddDays(2)));
    }

    [Fact]
    public void Return_Sets_ReturnedAt_And_Marks_Assignment_Inactive_When_Not_Previously_Returned()
    {
        var assignment = CreateAssignment();
        var returnedAt = FixedNow.AddDays(1);

        assignment.Return(returnedAt);

        Assert.Equal(returnedAt, assignment.ReturnedAt);
        Assert.False(assignment.IsActive);
    }

    [Fact]
    public void Return_Throws_When_Already_Returned()
    {
        var assignment = CreateAssignment();
        assignment.Return(FixedNow.AddDays(1));

        Assert.Throws<InvalidOperationException>(() => assignment.Return(FixedNow.AddDays(2)));
    }

    [Fact]
    public void IsActive_Is_True_Before_Return()
    {
        var assignment = CreateAssignment();

        Assert.True(assignment.IsActive);
    }
}
