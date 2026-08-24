using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Tests;

public class LeaveBalanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly AccrualStartDate = new(2026, 1, 1);

    private static LeaveBalance CreateBalance(decimal entitlementDays = 20m, DateOnly? accrualStartDate = null) =>
        LeaveBalance.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            2026, entitlementDays, accrualStartDate ?? AccrualStartDate, Now);

    [Fact]
    public void Create_Sets_UsedDays_And_AdjustmentDays_To_Zero()
    {
        var balance = CreateBalance(20m);

        Assert.Equal(0m, balance.UsedDays);
        Assert.Equal(0m, balance.AdjustmentDays);
        Assert.Equal(20m, balance.EntitlementDays);
    }

    [Fact]
    public void Create_Sets_AccrualStartDate()
    {
        var balance = CreateBalance(20m, new DateOnly(2026, 3, 15));

        Assert.Equal(new DateOnly(2026, 3, 15), balance.AccrualStartDate);
    }

    [Fact]
    public void RecalculateEntitlement_Updates_EntitlementDays_And_AccrualStartDate()
    {
        var balance = CreateBalance(20m, new DateOnly(2026, 1, 1));
        var later = Now.AddDays(1);

        balance.RecalculateEntitlement(14.66m, new DateOnly(2026, 6, 1), later);

        Assert.Equal(14.66m, balance.EntitlementDays);
        Assert.Equal(new DateOnly(2026, 6, 1), balance.AccrualStartDate);
        Assert.Equal(later, balance.UpdatedAt);
    }

    [Fact]
    public void RecalculateEntitlement_Does_Not_Reset_UsedDays_Or_AdjustmentDays()
    {
        var balance = CreateBalance(20m);
        balance.Adjust(2m, Now);
        balance.RecordUsage(3m, Now);

        balance.RecalculateEntitlement(14.66m, new DateOnly(2026, 6, 1), Now);

        Assert.Equal(2m, balance.AdjustmentDays);
        Assert.Equal(3m, balance.UsedDays);
    }

    [Fact]
    public void RemainingDays_Equals_Entitlement_Plus_Adjustment_Minus_Used()
    {
        var balance = CreateBalance(20m);
        balance.Adjust(2.5m, Now);
        balance.RecordUsage(5m, Now);

        Assert.Equal(17.5m, balance.RemainingDays);
    }

    [Fact]
    public void RemainingDays_Can_Go_Negative_When_Usage_Exceeds_Entitlement()
    {
        var balance = CreateBalance(5m);
        balance.RecordUsage(7m, Now);

        Assert.Equal(-2m, balance.RemainingDays);
    }

    [Fact]
    public void Adjust_With_Negative_Value_Reduces_AdjustmentDays()
    {
        var balance = CreateBalance(20m);
        balance.Adjust(-3m, Now);

        Assert.Equal(-3m, balance.AdjustmentDays);
    }

    [Fact]
    public void RecordUsage_Accumulates_Across_Multiple_Calls()
    {
        var balance = CreateBalance(20m);
        balance.RecordUsage(2m, Now);
        balance.RecordUsage(3m, Now);

        Assert.Equal(5m, balance.UsedDays);
    }

    [Fact]
    public void ReverseUsage_Subtracts_From_UsedDays()
    {
        var balance = CreateBalance(20m);
        balance.RecordUsage(5m, Now);
        balance.ReverseUsage(2m, Now);

        Assert.Equal(3m, balance.UsedDays);
    }

    [Fact]
    public void ReverseUsage_Clamps_At_Zero_When_Reversing_More_Than_Used()
    {
        var balance = CreateBalance(20m);
        balance.RecordUsage(3m, Now);
        balance.ReverseUsage(5m, Now);

        Assert.Equal(0m, balance.UsedDays);
    }

    [Fact]
    public void ReverseUsage_Exactly_Equal_To_UsedDays_Results_In_Zero()
    {
        var balance = CreateBalance(20m);
        balance.RecordUsage(4m, Now);
        balance.ReverseUsage(4m, Now);

        Assert.Equal(0m, balance.UsedDays);
    }

    [Fact]
    public void ReverseUsage_With_Zero_UsedDays_Stays_At_Zero()
    {
        var balance = CreateBalance(20m);
        balance.ReverseUsage(1m, Now);

        Assert.Equal(0m, balance.UsedDays);
    }
}
