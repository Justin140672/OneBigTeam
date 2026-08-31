using HR.Web.Components.Pages.Dashboards;

namespace HR.Web.Tests;

/// <summary>
/// DSH-07: pure key-to-next-index maths for the WAI-ARIA tabs keyboard pattern on the
/// operational dashboards (<see cref="DashboardTabKeyboard.NextIndex"/>).
/// </summary>
public class DashboardTabKeyboardTests
{
    [Theory]
    [InlineData("ArrowRight", 0, 3, 1)]
    [InlineData("ArrowRight", 1, 3, 2)]
    [InlineData("ArrowRight", 2, 3, 0)] // wraps past the last tab back to the first
    [InlineData("ArrowDown", 0, 3, 1)]  // ArrowDown is an alias for ArrowRight
    [InlineData("ArrowDown", 2, 3, 0)]
    public void NextIndex_ForwardKeys_AdvanceWithWrapAround(string key, int current, int count, int expected)
    {
        Assert.Equal(expected, DashboardTabKeyboard.NextIndex(key, current, count));
    }

    [Theory]
    [InlineData("ArrowLeft", 2, 3, 1)]
    [InlineData("ArrowLeft", 1, 3, 0)]
    [InlineData("ArrowLeft", 0, 3, 2)] // wraps past the first tab round to the last
    [InlineData("ArrowUp", 0, 3, 2)]   // ArrowUp is an alias for ArrowLeft
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
        var index = 0; // Pipeline

        index = DashboardTabKeyboard.NextIndex("ArrowRight", index, count)!.Value;
        Assert.Equal(1, index); // Activity

        index = DashboardTabKeyboard.NextIndex("ArrowRight", index, count)!.Value;
        Assert.Equal(2, index); // Insights

        index = DashboardTabKeyboard.NextIndex("ArrowRight", index, count)!.Value;
        Assert.Equal(0, index); // wrapped back to Pipeline

        index = DashboardTabKeyboard.NextIndex("End", index, count)!.Value;
        Assert.Equal(2, index); // jumped to Insights

        index = DashboardTabKeyboard.NextIndex("ArrowLeft", index, count)!.Value;
        Assert.Equal(1, index); // Activity

        index = DashboardTabKeyboard.NextIndex("Home", index, count)!.Value;
        Assert.Equal(0, index); // back to Pipeline
    }

    [Fact]
    public void NextIndex_SingleTab_ForwardAndBackwardStayOnTheOnlyTab()
    {
        Assert.Equal(0, DashboardTabKeyboard.NextIndex("ArrowRight", 0, 1));
        Assert.Equal(0, DashboardTabKeyboard.NextIndex("ArrowLeft", 0, 1));
    }
}
