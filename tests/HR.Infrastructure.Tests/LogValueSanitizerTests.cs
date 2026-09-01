using HR.Infrastructure.Logging;

namespace HR.Infrastructure.Tests;

/// <summary>
/// Ticket 3: the sanitiser that neutralises user-controlled values before they reach a log sink.
/// </summary>
public class LogValueSanitizerTests
{
    private const char Sep2028 = (char)0x2028;
    private const char Sep2029 = (char)0x2029;
    private const char Replacement = (char)0xFFFD;

    [Theory]
    [InlineData("/api/users\r\nFATAL forged")]
    [InlineData("a\rb")]
    [InlineData("a\nb")]
    [InlineData("a\tb")]
    [InlineData("a\u0000b")]
    public void Removes_control_characters(string input)
    {
        var result = LogValueSanitizer.Sanitize(input);

        Assert.All(result, c => Assert.False(char.IsControl(c)));
        Assert.DoesNotContain('\r', result);
        Assert.DoesNotContain('\n', result);
        Assert.DoesNotContain('\t', result);
    }

    [Fact]
    public void Removes_unicode_line_and_paragraph_separators()
    {
        var result = LogValueSanitizer.Sanitize($"a{Sep2028}b{Sep2029}c");

        Assert.DoesNotContain(Sep2028, result);
        Assert.DoesNotContain(Sep2029, result);
        Assert.Equal($"a{Replacement}b{Replacement}c", result);
    }

    [Fact]
    public void Crlf_forged_line_is_collapsed_to_a_single_line()
    {
        var result = LogValueSanitizer.Sanitize("/api/x\r\nFATAL breach");

        Assert.Equal($"/api/x{Replacement}{Replacement}FATAL breach", result);
    }

    [Fact]
    public void Leaves_clean_values_untouched()
    {
        Assert.Equal("/api/companies/123", LogValueSanitizer.Sanitize("/api/companies/123"));
    }

    [Fact]
    public void Empty_and_null_become_empty_string()
    {
        Assert.Equal(string.Empty, LogValueSanitizer.Sanitize(null));
        Assert.Equal(string.Empty, LogValueSanitizer.Sanitize(string.Empty));
    }

    [Fact]
    public void Long_values_are_truncated()
    {
        var result = LogValueSanitizer.Sanitize(new string('x', 5000));

        Assert.True(result.Length < 5000);
        Assert.EndsWith("...", result);
    }
}
