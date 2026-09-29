using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Tests;

public class EmployeeTimelineVisibilityResolverTests
{

    [Fact]
    public void HrOnly_Is_Visible_To_Hr()
    {
        Assert.True(EmployeeTimelineVisibilityResolver.CanView(
            EmployeeTimelineVisibility.HrOnly, viewerIsHr: true, viewerIsSelf: false, viewerIsManager: false));
    }

    [Fact]
    public void HrOnly_Is_Not_Visible_To_Self()
    {
        Assert.False(EmployeeTimelineVisibilityResolver.CanView(
            EmployeeTimelineVisibility.HrOnly, viewerIsHr: false, viewerIsSelf: true, viewerIsManager: false));
    }

    [Fact]
    public void HrOnly_Is_Not_Visible_To_Manager()
    {
        Assert.False(EmployeeTimelineVisibilityResolver.CanView(
            EmployeeTimelineVisibility.HrOnly, viewerIsHr: false, viewerIsSelf: false, viewerIsManager: true));
    }

    [Fact]
    public void HrOnly_Is_Not_Visible_To_Unrelated_Viewer()
    {
        Assert.False(EmployeeTimelineVisibilityResolver.CanView(
            EmployeeTimelineVisibility.HrOnly, viewerIsHr: false, viewerIsSelf: false, viewerIsManager: false));
    }


    [Fact]
    public void EmployeeAndHr_Is_Visible_To_Hr()
    {
        Assert.True(EmployeeTimelineVisibilityResolver.CanView(
            EmployeeTimelineVisibility.EmployeeAndHr, viewerIsHr: true, viewerIsSelf: false, viewerIsManager: false));
    }

    [Fact]
    public void EmployeeAndHr_Is_Visible_To_Self()
    {
        Assert.True(EmployeeTimelineVisibilityResolver.CanView(
            EmployeeTimelineVisibility.EmployeeAndHr, viewerIsHr: false, viewerIsSelf: true, viewerIsManager: false));
    }

    [Fact]
    public void EmployeeAndHr_Is_Not_Visible_To_Manager_Alone()
    {
        Assert.False(EmployeeTimelineVisibilityResolver.CanView(
            EmployeeTimelineVisibility.EmployeeAndHr, viewerIsHr: false, viewerIsSelf: false, viewerIsManager: true));
    }

    [Fact]
    public void EmployeeAndHr_Is_Not_Visible_To_Unrelated_Viewer()
    {
        Assert.False(EmployeeTimelineVisibilityResolver.CanView(
            EmployeeTimelineVisibility.EmployeeAndHr, viewerIsHr: false, viewerIsSelf: false, viewerIsManager: false));
    }


    [Fact]
    public void AuthorisedInternal_Is_Visible_To_Hr()
    {
        Assert.True(EmployeeTimelineVisibilityResolver.CanView(
            EmployeeTimelineVisibility.AuthorisedInternal, viewerIsHr: true, viewerIsSelf: false, viewerIsManager: false));
    }

    [Fact]
    public void AuthorisedInternal_Is_Visible_To_Self()
    {
        Assert.True(EmployeeTimelineVisibilityResolver.CanView(
            EmployeeTimelineVisibility.AuthorisedInternal, viewerIsHr: false, viewerIsSelf: true, viewerIsManager: false));
    }

    [Fact]
    public void AuthorisedInternal_Is_Visible_To_Manager()
    {
        Assert.True(EmployeeTimelineVisibilityResolver.CanView(
            EmployeeTimelineVisibility.AuthorisedInternal, viewerIsHr: false, viewerIsSelf: false, viewerIsManager: true));
    }

    [Fact]
    public void AuthorisedInternal_Is_Not_Visible_To_Unrelated_Viewer()
    {
        Assert.False(EmployeeTimelineVisibilityResolver.CanView(
            EmployeeTimelineVisibility.AuthorisedInternal, viewerIsHr: false, viewerIsSelf: false, viewerIsManager: false));
    }


    [Fact]
    public void Unrelated_Viewer_Never_Sees_HrOnly_Tier()
    {
        Assert.False(EmployeeTimelineVisibilityResolver.CanView(
            EmployeeTimelineVisibility.HrOnly, viewerIsHr: false, viewerIsSelf: false, viewerIsManager: false));
    }

    [Fact]
    public void Unrelated_Viewer_Never_Sees_EmployeeAndHr_Tier()
    {
        Assert.False(EmployeeTimelineVisibilityResolver.CanView(
            EmployeeTimelineVisibility.EmployeeAndHr, viewerIsHr: false, viewerIsSelf: false, viewerIsManager: false));
    }

    [Fact]
    public void Unrelated_Viewer_Never_Sees_AuthorisedInternal_Tier()
    {
        Assert.False(EmployeeTimelineVisibilityResolver.CanView(
            EmployeeTimelineVisibility.AuthorisedInternal, viewerIsHr: false, viewerIsSelf: false, viewerIsManager: false));
    }
}
