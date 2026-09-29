using HR.Web.Components.Pages.Dashboards;

namespace HR.Web.Tests;

public class DashboardTabKeyboardTests
{
    [Theory]
    [InlineData("ArrowRight", 0, 3, 1)]
    [InlineData("ArrowRight", 1, 3, 2)]
    [InlineData("ArrowRight", 2, 3, 0)]
    [InlineData("ArrowDown", 0, 3, 1)]
    [InlineData("ArrowDown", 2, 3, 0)]
    public void NextIndex_ForwardKeys_AdvanceWithWrapAround(string key, int current, int count, int expected)
    {
        Assert.Equal(expected, DashboardTabKeyboard.NextIndex(key, current, count));
    }

    [Theory]
    [InlineData("ArrowLeft", 2, 3, 1)]
    [InlineData("ArrowLeft", 1, 3, 0)]
    [InlineData("ArrowLeft", 0, 3, 2)]
    [InlineData("ArrowUp", 0, 3, 2)]
    [InlineData("ArrowUp", 2, 3, 1)]
    public void NextIndex_BackwardKeys_RetreatWithWrapAround(string key, int current, int count, int expected)
    {
        Assert.Equal(expected, DashboardTabKeyboard.NextIndex(key, current, count));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NextIndex_Home_SelectsFirstTab_RegardlessOfCurrent(int current)
    {
        Assert.Equal(0, DashboardTabKeyboard.NextIndex("Home", current, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NextIndex_End_SelectsLastTab_RegardlessOfCurrent(int current)
    {
        Assert.Equal(2, DashboardTabKeyboard.NextIndex("End", current, 3));
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData(" ")]
    [InlineData("Tab")]
    [InlineData("a")]
    [InlineData("")]
    public void NextIndex_UnhandledKey_ReturnsNull(string key)
    {
        Assert.Null(DashboardTabKeyboard.NextIndex(key, 1, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5)]
    public void NextIndex_NonPositiveTabCount_ReturnsNull_EvenForHandledKeys(int tabCount)
    {
        Assert.Null(DashboardTabKeyboard.NextIndex("ArrowRight", 0, tabCount));
        Assert.Null(DashboardTabKeyboard.NextIndex("Home", 0, tabCount));
        Assert.Null(DashboardTabKeyboard.NextIndex("End", 0, tabCount));
    }

    [Fact]
    public void NextIndex_RealisticThreeTabSequence_PipelineActivityInsights()
    {
        const int count = 3;
        var index = 0;

        index = DashboardTabKeyboard.NextIndex("ArrowRight", index, count)!.Value;
        Assert.Equal(1, index);

        index = DashboardTabKeyboard.NextIndex("ArrowRight", index, count)!.Value;
        Assert.Equal(2, index);

        index = DashboardTabKeyboard.NextIndex("ArrowRight", index, count)!.Value;
        Assert.Equal(0, index);

        index = DashboardTabKeyboard.NextIndex("End", index, count)!.Value;
        Assert.Equal(2, index);

        index = DashboardTabKeyboard.NextIndex("ArrowLeft", index, count)!.Value;
        Assert.Equal(1, index);

        index = DashboardTabKeyboard.NextIndex("Home", index, count)!.Value;
        Assert.Equal(0, index);
    }

    [Fact]
    public void NextIndex_SingleTab_ForwardAndBackwardStayOnTheOnlyTab()
    {
        Assert.Equal(0, DashboardTabKeyboard.NextIndex("ArrowRight", 0, 1));
        Assert.Equal(0, DashboardTabKeyboard.NextIndex("ArrowLeft", 0, 1));
    }
}
