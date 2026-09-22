using HR.Modules.Documents.Services;
using Xunit;

namespace HR.Modules.Documents.Tests;

/// <summary>
/// Reliability review issue 3 (P1): startup previously only checked that ClamAv:Host was
/// non-empty; this proves the port range and timeout bound are now enforced too.
/// </summary>
public class ClamAvOptionsValidatorTests
{
    private static ClamAvOptions ValidOptions() => new()
    {
        Host = "clamav.internal",
        Port = 3310,
        TimeoutSeconds = 30,
    };

    private static readonly ClamAvOptionsValidator Validator = new();

    [Fact]
    public void Valid_Options_Pass()
    {
        var result = Validator.Validate(null, ValidOptions());
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Missing_Host_Fails()
    {
        var options = ValidOptions();
        options.Host = "";

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Host", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(100000)]
    public void Out_Of_Range_Port_Fails(int port)
    {
        var options = ValidOptions();
        options.Port = port;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Port", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3310)]
    [InlineData(65535)]
    public void InRange_Port_Passes(int port)
    {
        var options = ValidOptions();
        options.Port = port;

        var result = Validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositive_Timeout_Fails(int timeout)
    {
        var options = ValidOptions();
        options.TimeoutSeconds = timeout;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("TimeoutSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Excessive_Timeout_Fails()
    {
        var options = ValidOptions();
        options.TimeoutSeconds = 601;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
    }
}
