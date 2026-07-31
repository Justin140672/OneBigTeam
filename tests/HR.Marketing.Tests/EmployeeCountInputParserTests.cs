using HR.Marketing.Services;

namespace HR.Marketing.Tests;

public class EmployeeCountInputParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyInput_ReturnsZeroWithNoValidationMessage(string? raw)
    {
        var result = EmployeeCountInputParser.Parse(raw, fallback: 75);

        Assert.Equal(0, result.Value);
        Assert.Null(result.ValidationMessage);
    }

    [Theory]
    [InlineData("10.5")]
    [InlineData("10,5")]
    public void Parse_DecimalValue_ReturnsFallbackWithValidationMessage(string raw)
    {
        var result = EmployeeCountInputParser.Parse(raw, fallback: 75);

        Assert.Equal(75, result.Value);
        Assert.NotNull(result.ValidationMessage);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("ten")]
    [InlineData("--5")]
    public void Parse_NonNumericValue_ReturnsFallbackWithValidationMessage(string raw)
    {
        var result = EmployeeCountInputParser.Parse(raw, fallback: 75);

        Assert.Equal(75, result.Value);
        Assert.NotNull(result.ValidationMessage);
    }

    [Fact]
    public void Parse_NegativeValue_ReturnsFallbackWithValidationMessage()
    {
        var result = EmployeeCountInputParser.Parse("-5", fallback: 75);

        Assert.Equal(75, result.Value);
        Assert.NotNull(result.ValidationMessage);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("75", 75)]
    [InlineData("500", 500)]
    public void Parse_ValidWholeNumber_ReturnsValueWithNoValidationMessage(string raw, int expected)
    {
        var result = EmployeeCountInputParser.Parse(raw, fallback: 999);

        Assert.Equal(expected, result.Value);
        Assert.Null(result.ValidationMessage);
    }
}
