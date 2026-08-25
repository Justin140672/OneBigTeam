using HR.Modules.Reporting.ReportRegistry;

namespace HR.Modules.Reporting.Tests;

public class ReportFilterValidatorTests
{
    private static ReportDefinition GetDefinition(string reportId)
    {
        var found = ReportCatalog.TryGet(reportId, out var definition);
        Assert.True(found);
        return definition;
    }

    [Fact]
    public void Validate_Succeeds_For_Minimal_Empty_Object()
    {
        var definition = GetDefinition("employee-directory");

        var result = ReportFilterValidator.Validate(definition, "{}");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_Fails_For_Unknown_Field_Name()
    {
        var definition = GetDefinition("employee-directory");

        var result = ReportFilterValidator.Validate(definition, "{\"NotARealField\":1}");

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Validate_Fails_For_Value_Not_In_Enum_Restricted_Allowed_Values()
    {
        var definition = GetDefinition("leave-summary");

        var result = ReportFilterValidator.Validate(definition, "{\"GroupBy\":\"NotAValue\"}");

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Validate_Succeeds_For_Valid_Enum_Restricted_Value()
    {
        var definition = GetDefinition("leave-summary");

        var result = ReportFilterValidator.Validate(definition, "{\"GroupBy\":\"Department\"}");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_Fails_Cleanly_For_Malformed_Json()
    {
        var definition = GetDefinition("employee-directory");

        var result = ReportFilterValidator.Validate(definition, "{not json");

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Validate_Fails_When_Json_Exceeds_Max_Length()
    {
        var definition = GetDefinition("employee-directory");
        var oversized = "{\"a\":\"" + new string('x', ReportFilterValidator.MaxFilterCriteriaJsonLength) + "\"}";

        var result = ReportFilterValidator.Validate(definition, oversized);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Validate_Succeeds_At_Exactly_Max_Length()
    {
        var definition = GetDefinition("employee-directory");
        // Build a JSON string of exactly MaxFilterCriteriaJsonLength characters using a known field.
        var prefix = "{\"DepartmentId\":\"";
        var suffix = "\"}";
        var padding = ReportFilterValidator.MaxFilterCriteriaJsonLength - prefix.Length - suffix.Length;
        Assert.True(padding > 0);
        var json = prefix + new string('a', padding) + suffix;
        Assert.Equal(ReportFilterValidator.MaxFilterCriteriaJsonLength, json.Length);

        var result = ReportFilterValidator.Validate(definition, json);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_Fails_For_Null_Empty_String()
    {
        var definition = GetDefinition("employee-directory");

        var result = ReportFilterValidator.Validate(definition, null!);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Validate_Fails_For_Empty_String()
    {
        var definition = GetDefinition("employee-directory");

        var result = ReportFilterValidator.Validate(definition, string.Empty);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Validate_Fails_For_Whitespace_Only_String()
    {
        var definition = GetDefinition("employee-directory");

        var result = ReportFilterValidator.Validate(definition, "   ");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Validate_Fails_When_Root_Is_A_Json_Array_Not_An_Object()
    {
        var definition = GetDefinition("employee-directory");

        var result = ReportFilterValidator.Validate(definition, "[1,2,3]");

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Validate_Accepts_Null_Valued_Property_For_A_Known_Field_Without_Checking_Allowed_Values()
    {
        var definition = GetDefinition("leave-summary");

        var result = ReportFilterValidator.Validate(definition, "{\"GroupBy\":null}");

        Assert.True(result.IsSuccess);
    }
}
