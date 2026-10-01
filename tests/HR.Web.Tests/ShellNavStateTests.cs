using HR.Web.Services;

namespace HR.Web.Tests;

public class ShellNavStateTests
{
    [Theory]
    [InlineData(1920, ShellLayoutMode.Wide)]
    [InlineData(1440, ShellLayoutMode.Wide)]
    [InlineData(1200, ShellLayoutMode.Wide)]
    [InlineData(1199, ShellLayoutMode.Compact)]
    [InlineData(1024, ShellLayoutMode.Compact)]
    [InlineData(850, ShellLayoutMode.Compact)]
    [InlineData(768, ShellLayoutMode.Compact)]
    [InlineData(767, ShellLayoutMode.Overlay)]
    [InlineData(390, ShellLayoutMode.Overlay)]
    public void ForWidth_Maps_Viewport_To_Shell_Mode(double width, ShellLayoutMode expected)
    {
        Assert.Equal(expected, ShellBreakpoints.ForWidth(width));
    }

    [Fact]
    public void Wide_Mode_Honours_The_Remembered_Expanded_Preference()
    {
        var state = new ShellNavState();
        state.RestorePreference(true);
        state.SetMode(ShellLayoutMode.Wide);

        Assert.True(state.IsOpen);
        Assert.False(state.UsesDrawer);

        state.Toggle();

        Assert.False(state.IsOpen);
        Assert.False(state.PreferenceExpanded);
    }

    [Theory]
    [InlineData(ShellLayoutMode.Compact)]
    [InlineData(ShellLayoutMode.Overlay)]
    public void Constrained_Modes_Start_Collapsed_Even_When_Preference_Is_Expanded(ShellLayoutMode mode)
    {
        var state = new ShellNavState();
        state.RestorePreference(true);
        state.SetMode(mode);

        Assert.False(state.IsOpen);
        Assert.True(state.UsesDrawer);
        Assert.True(state.PreferenceExpanded);
    }

    [Fact]
    public void Opening_The_Drawer_Does_Not_Change_The_Remembered_Preference()
    {
        var state = new ShellNavState();
        state.RestorePreference(false);
        state.SetMode(ShellLayoutMode.Compact);

        state.Toggle();

        Assert.True(state.IsOpen);
        Assert.False(state.PreferenceExpanded);
    }

    [Fact]
    public void Returning_To_Wide_Restores_The_Expanded_Preference_And_Closes_The_Drawer()
    {
        var state = new ShellNavState();
        state.RestorePreference(true);
        state.SetMode(ShellLayoutMode.Compact);
        state.Toggle();

        var changed = state.SetMode(ShellLayoutMode.Wide);

        Assert.True(changed);
        Assert.False(state.DrawerOpen);
        Assert.True(state.IsOpen);
    }

    [Fact]
    public void Narrowing_The_Viewport_Closes_An_Expanded_Sidebar_Without_Losing_The_Preference()
    {
        var state = new ShellNavState();
        state.RestorePreference(true);
        state.SetMode(ShellLayoutMode.Wide);

        state.SetMode(ShellLayoutMode.Overlay);

        Assert.False(state.IsOpen);
        Assert.True(state.PreferenceExpanded);
    }

    [Fact]
    public void Navigation_Closes_The_Drawer_But_Not_The_Wide_Sidebar()
    {
        var state = new ShellNavState();
        state.SetMode(ShellLayoutMode.Compact);
        state.Toggle();

        Assert.True(state.CloseDrawer());
        Assert.False(state.IsOpen);

        state.SetMode(ShellLayoutMode.Wide);
        Assert.False(state.CloseDrawer());
        Assert.True(state.IsOpen);
    }

    [Fact]
    public void Setting_The_Same_Mode_Again_Is_A_No_Op_That_Keeps_The_Drawer_Open()
    {
        var state = new ShellNavState();
        state.SetMode(ShellLayoutMode.Compact);
        state.Toggle();

        Assert.False(state.SetMode(ShellLayoutMode.Compact));
        Assert.True(state.DrawerOpen);
    }
}
