using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Tests;

public class LeavePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Defaults_RequiresApproval_To_True_When_Unspecified()
    {
        var policy = LeavePolicy.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Standard Policy", null, 5, false, false, Now);

        Assert.True(policy.RequiresApproval);
    }

    [Fact]
    public void Create_Sets_RequiresApproval_Explicitly_To_False()
    {
        var policy = LeavePolicy.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Auto-approve Policy", null, 5, false, false, Now, requiresApproval: false);

        Assert.False(policy.RequiresApproval);
    }

    [Fact]
    public void Update_Sets_RequiresApproval_Explicitly()
    {
        var policy = LeavePolicy.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Standard Policy", null, 5, false, false, Now);
        Assert.True(policy.RequiresApproval);

        policy.Update("Standard Policy", "Updated description", 5, false, false, Now.AddDays(1));

        Assert.False(policy.RequiresApproval);
    }

    [Fact]
    public void Update_Can_Re_Enable_RequiresApproval()
    {
        var policy = LeavePolicy.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Auto-approve Policy", null, 5, false, false, Now, requiresApproval: false);
        Assert.False(policy.RequiresApproval);

        policy.Update("Auto-approve Policy", null, 5, false, true, Now.AddDays(1));

        Assert.True(policy.RequiresApproval);
    }
}
