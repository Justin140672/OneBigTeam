using HR.Modules.DataImport.Services;

namespace HR.Modules.DataImport.Tests;

public class ColumnMappingProfileTests
{
    [Fact]
    public void WithOverrides_Overriding_An_Existing_Target_Field_Changes_Its_Header()
    {
        var overrides = new Dictionary<string, string> { ["FirstName"] = "Given Name" };

        var result = StandardEmployeeColumnMapping.Default.WithOverrides(overrides);

        Assert.Equal("Given Name", result.TargetFieldToHeaderName["FirstName"]);
    }

    [Fact]
    public void WithOverrides_Target_Field_Not_Mentioned_In_Overrides_Keeps_Default_Header()
    {
        var overrides = new Dictionary<string, string> { ["FirstName"] = "Given Name" };

        var result = StandardEmployeeColumnMapping.Default.WithOverrides(overrides);

        Assert.Equal("Last Name", result.TargetFieldToHeaderName["LastName"]);
        Assert.Equal("Work Email", result.TargetFieldToHeaderName["WorkEmail"]);
    }

    [Fact]
    public void WithOverrides_Null_Overrides_Returns_Equivalent_Unchanged_Mapping()
    {
        var expected = StandardEmployeeColumnMapping.Default.TargetFieldToHeaderName;

        var result = StandardEmployeeColumnMapping.Default.WithOverrides(null);

        Assert.Equal(expected.Count, result.TargetFieldToHeaderName.Count);
        foreach (var (targetField, headerName) in expected)
            Assert.Equal(headerName, result.TargetFieldToHeaderName[targetField]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithOverrides_Empty_Or_Whitespace_Header_Value_Is_Ignored(string headerValue)
    {
        var overrides = new Dictionary<string, string> { ["FirstName"] = headerValue };

        var result = StandardEmployeeColumnMapping.Default.WithOverrides(overrides);

        Assert.Equal("First Name", result.TargetFieldToHeaderName["FirstName"]);
    }
}
