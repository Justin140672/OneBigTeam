using HR.Web.Models;

namespace HR.Web.Tests;

public class TaskTitleFormatterTests
{
    [Fact]
    public void StripEmployeeSuffix_Removes_Matching_Employee_Name_Suffix()
    {
        var result = TaskTitleFormatter.StripEmployeeSuffix("Send welcome email — Ada Lovelace", "Ada Lovelace");

        Assert.Equal("Send welcome email", result);
    }

    [Fact]
    public void StripEmployeeSuffix_Matches_Any_Supplied_Name_Variant_Ignoring_Case()
    {
        var result = TaskTitleFormatter.StripEmployeeSuffix(
            "Set up workstation — ada lovelace", "Ada Byron", "Ada Lovelace");

        Assert.Equal("Set up workstation", result);
    }

    [Fact]
    public void StripEmployeeSuffix_Leaves_Title_Unchanged_For_Another_Employee()
    {
        var title = "Send welcome email — Grace Hopper";

        Assert.Equal(title, TaskTitleFormatter.StripEmployeeSuffix(title, "Ada Lovelace"));
    }

    [Fact]
    public void StripEmployeeSuffix_Leaves_Title_Unchanged_When_No_Names_Supplied()
    {
        var title = "Send welcome email — Ada Lovelace";

        Assert.Equal(title, TaskTitleFormatter.StripEmployeeSuffix(title));
    }

    [Fact]
    public void StripEmployeeSuffix_Does_Not_Strip_Name_That_Is_The_Whole_Title()
    {
        Assert.Equal(" — Ada", TaskTitleFormatter.StripEmployeeSuffix(" — Ada", "Ada"));
    }
}
