using HR.Modules.Notifications.Features.ResolveOperationalAlert;

namespace HR.Modules.Notifications.Tests.Features;

public class ResolveOperationalAlertValidatorTests
{
    private static readonly ResolveOperationalAlertValidator Validator = new();

    private static ResolveOperationalAlertRequest Request(Guid? alertId = null, string? note = "Resolved after fix") =>
        new(alertId ?? Guid.NewGuid(), note);

    [Fact]
    public void Valid_Request_Passes()
    {
        Assert.True(Validator.Validate(Request()).IsValid);
    }

    [Fact]
    public void Empty_AlertId_Fails()
    {
        var result = Validator.Validate(Request(alertId: Guid.Empty));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResolveOperationalAlertRequest.AlertId));
    }

    [Fact]
    public void Null_Note_Fails()
    {
        Assert.False(Validator.Validate(Request(note: null)).IsValid);
    }

    [Fact]
    public void Empty_Note_Fails()
    {
        Assert.False(Validator.Validate(Request(note: string.Empty)).IsValid);
    }

    [Fact]
    public void Whitespace_Only_Note_Fails()
    {
        Assert.False(Validator.Validate(Request(note: "        ")).IsValid);
    }

    [Theory]
    [InlineData(4, false)]   // below the trimmed minimum of 5
    [InlineData(5, true)]    // exact lower boundary
    [InlineData(1000, true)] // exact upper boundary
    [InlineData(1001, false)] // one past the upper boundary
    public void Trimmed_Length_Boundary(int length, bool expectValid)
    {
        var note = new string('x', length);
        Assert.Equal(expectValid, Validator.Validate(Request(note: note)).IsValid);
    }

    [Fact]
    public void Note_Is_Measured_After_Trimming_Surrounding_Whitespace()
    {
        // 3 real characters padded to well over 5 with whitespace — still too short once trimmed.
        var result = Validator.Validate(Request(note: "   abc   "));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Note_At_Upper_Boundary_With_Surrounding_Whitespace_Passes_After_Trim()
    {
        var note = "  " + new string('x', 1000) + "  ";
        Assert.True(Validator.Validate(Request(note: note)).IsValid);
    }
}
